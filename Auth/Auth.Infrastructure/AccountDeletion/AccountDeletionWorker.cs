using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.AccountDeletion;

/// <summary>
/// Background executor of the two-phase deletion (pattern:
/// <c>NotificationOutboxDispatcher</c>): reclaims orphaned Processing rows at
/// startup (single-worker topology — any Processing row at startup is a crash
/// artifact), polls for requests whose grace window elapsed and executes each
/// through <see cref="ExecuteAccountDeletionCommand"/>, alarms when a request
/// stays pending more than 24 hours past its deadline, and runs the daily
/// retention/destruction sweep (KVKK's ≤ 6-month cadence, with margin):
/// restore re-apply, expired OTPs, anonymized login-attempt retention and
/// delivered-mail retention. Like the outbox, it depends on the app pool
/// staying loaded (preload/always-running).
/// </summary>
public class AccountDeletionWorker : BackgroundService
{
    private static readonly TimeSpan OverdueAlarmThreshold = TimeSpan.FromHours(24);
    private const int SweepReapplyBatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AccountDeletionSettings> _settings;
    private readonly ILogger<AccountDeletionWorker> _logger;

    // Default(DateOnly) is far in the past, so the first cycle always sweeps —
    // the startup catch-up the restore runbook depends on.
    private DateOnly _lastSweepDateUtc;

    public AccountDeletionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AccountDeletionSettings> settings,
        ILogger<AccountDeletionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Account deletion worker started (poll {PollMinutes}m, batch {BatchSize}, max attempts {MaxAttempts}).",
            _settings.CurrentValue.WorkerPollMinutes, _settings.CurrentValue.WorkerBatchSize, _settings.CurrentValue.MaxExecutionAttempts);

        await SafeReclaimAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteDuePassAsync(stoppingToken);
                await RunDailySweepIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Account deletion worker cycle failed; retrying on the next poll");
            }

            try
            {
                await Task.Delay(_settings.CurrentValue.WorkerPollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Executes every due request and raises the compliance alarm for any
    /// request still pending more than 24 hours past its grace deadline.
    /// </summary>
    public async Task ExecuteDuePassAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var requestRepository = scope.ServiceProvider.GetRequiredService<IAccountDeletionRequestRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var due = await requestRepository.GetDueAsync(
            DateTime.UtcNow, _settings.CurrentValue.WorkerBatchSize, cancellationToken);

        foreach (var request in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claim races (recovery won) and execution failures are the
            // command's concern; the worker only records the outcome.
            var result = await sender.Send(new ExecuteAccountDeletionCommand(request.Id), cancellationToken);
            if (result.IsError)
            {
                _logger.LogInformation(
                    "Deletion request {RequestId} not executed this pass: {Error}",
                    request.Id, result.FirstError.Code);
            }
        }

        // A request whose grace ended over 24h ago and is STILL pending after
        // the pass means executions are persistently failing (or dead-lettered
        // work is piling up) — a compliance deadline is at risk.
        var overdue = await requestRepository.GetDueAsync(
            DateTime.UtcNow - OverdueAlarmThreshold, 1, cancellationToken);
        if (overdue.Count > 0)
        {
            _logger.LogError(
                "At least one account deletion request is more than {Hours}h past its grace deadline and still pending — investigate failing executions",
                OverdueAlarmThreshold.TotalHours);
        }
    }

    /// <summary>
    /// Runs the retention sweep once per UTC day (the date only advances on a
    /// successful sweep, so a failed one retries on the next poll).
    /// </summary>
    public async Task RunDailySweepIfDueAsync(CancellationToken cancellationToken)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (todayUtc == _lastSweepDateUtc)
        {
            return;
        }

        await RunSweepAsync(cancellationToken);
        _lastSweepDateUtc = todayUtc;
    }

    /// <summary>
    /// The retention/destruction sweep: re-applies destruction for accounts a
    /// backup restore resurrected, purges expired deletion OTPs, enforces the
    /// login-attempt retention and deletes delivered outbox mail past its
    /// retention. Idempotent throughout.
    /// </summary>
    public async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var requestRepository = scope.ServiceProvider.GetRequiredService<IAccountDeletionRequestRepository>();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var credentialRevocation = scope.ServiceProvider.GetRequiredService<ICredentialRevocationService>();
        var verificationRepository = scope.ServiceProvider.GetRequiredService<IAccountDeletionVerificationRepository>();
        var loginAttemptRepository = scope.ServiceProvider.GetRequiredService<ILoginAttemptRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var tombstoneRepository = scope.ServiceProvider.GetRequiredService<IAccountDeletionTombstoneRepository>();
        var secretChallengeRepository = scope.ServiceProvider.GetRequiredService<ISecretOperationChallengeRepository>();

        // 1) Restore re-apply: a Completed request with a live user row means
        //    a backup restore resurrected destroyed data (R5).
        var resurrected = await requestRepository.GetCompletedWithLiveUserAsync(
            SweepReapplyBatchSize, cancellationToken);
        var reapplied = 0;
        foreach (var request in resurrected)
        {
            try
            {
                _logger.LogWarning(
                    "Re-applying destruction for restored account {UserId} (request {RequestId})",
                    request.UserId, request.Id);

                // Restored session/token rows must die with the account; the
                // purge requires the soft-deleted flag before it runs.
                await credentialRevocation.RevokeAllCredentialsAsync(
                    request.UserId, null, "Destruction re-applied after restore", cancellationToken);
                await userRepository.DeleteAsync(request.UserId, cancellationToken);
                if (await userRepository.HardDeleteAsync(request.UserId, cancellationToken))
                {
                    reapplied++;
                    await auditLogRepository.CreateAsync(
                        Auth.Domain.Entities.AuditLog.CreateSuccess(
                            actionType: "UserManagement",
                            action: "user.deletion_reapplied",
                            userId: WellKnownUserIds.System,
                            entityType: "User",
                            entityId: request.UserId,
                            additionalData: $"{{\"policyVersion\":\"{request.PolicyVersion}\"}}"),
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to re-apply destruction for restored account {UserId}; retrying next sweep",
                    request.UserId);
            }
        }

        // 2) Expired/used deletion OTPs (short-lived Class A rows).
        await verificationRepository.DeleteExpiredAsync(cancellationToken);

        // 2b) Dead secret step-up challenges. Same class of row as the deletion
        //     OTPs above: a short-lived credential with no purpose once it can
        //     neither be answered nor spent. Without this the table only grew —
        //     its own definition promised this sweep and nothing performed it.
        var purgedSecretChallenges = await secretChallengeRepository.DeleteExpiredAsync(cancellationToken);

        // 3) Anonymized login attempts past the fraud-analysis retention.
        await loginAttemptRepository.CleanupOldAttemptsAsync(
            DateTime.UtcNow.AddDays(-_settings.CurrentValue.LoginAttemptRetentionDays), cancellationToken);

        // 4) Delivered mail past its retention (rendered recipient PII).
        var purgedOutbox = await outboxRepository.DeleteSentOlderThanAsync(
            DateTime.UtcNow.AddDays(-_settings.CurrentValue.OutboxRetentionDays), cancellationToken);

        // 5) Audit history past its retention. Runs LAST so the sweep's own
        //    audit entry below is never inside the window it just purged.
        //    Without this call the table had no end of life at all: the
        //    published policy states a three-year floor and never a ceiling,
        //    and an identifier released after a finite reservation window
        //    could still be linked through audit rows that outlive it.
        var auditCutoff = DateTime.UtcNow.AddDays(-_settings.CurrentValue.AuditLogRetentionDays);
        await auditLogRepository.CleanupOldLogsAsync(auditCutoff, cancellationToken);

        // 6) Identifier reservations whose window has elapsed. Deleting the row
        //    IS the erasure of a keyed digest that no key destruction can reach
        //    while live reservations still need that key. EffectiveIdentifier-
        //    ReservationDays is never shorter than the audit retention, so an
        //    address is never released while records keyed to it survive.
        var reservationCutoff = DateTime.UtcNow.AddDays(
            -_settings.CurrentValue.EffectiveIdentifierReservationDays);
        var releasedReservations = await tombstoneRepository.DeleteExpiredAsync(
            reservationCutoff, cancellationToken);

        await auditLogRepository.CreateAsync(
            Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: "System",
                action: "system.retention_sweep",
                userId: WellKnownUserIds.System,
                additionalData:
                    $"{{\"reappliedDeletions\":{reapplied},\"purgedOutboxRows\":{purgedOutbox}," +
                    $"\"auditCutoffUtc\":\"{auditCutoff:O}\"," +
                    $"\"purgedSecretChallenges\":{purgedSecretChallenges}," +
                    $"\"releasedReservations\":{releasedReservations}}}"),
            cancellationToken);

        _logger.LogInformation(
            "Retention sweep complete: {Reapplied} re-applied destructions, {PurgedOutbox} delivered outbox rows purged, audit history trimmed before {AuditCutoff:u}, {Released} identifier reservations released",
            reapplied, purgedOutbox, auditCutoff, releasedReservations);
    }

    private async Task SafeReclaimAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var requestRepository = scope.ServiceProvider.GetRequiredService<IAccountDeletionRequestRepository>();
            var reclaimed = await requestRepository.ReclaimProcessingAsync(cancellationToken);
            if (reclaimed > 0)
            {
                _logger.LogWarning(
                    "Reclaimed {Count} deletion request(s) orphaned in Processing by a previous worker instance",
                    reclaimed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never block startup on the reclaim; orphans are retried when the
            // next reclaim succeeds.
            _logger.LogError(ex, "Failed to reclaim orphaned deletion requests at startup");
        }
    }
}
