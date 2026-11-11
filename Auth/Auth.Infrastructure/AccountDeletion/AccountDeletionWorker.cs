using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
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
/// <remarks>
/// The sweep writes to the audit log only when it changed something or failed.
/// It used to write a row on every run, including the many runs that deleted
/// nothing (one per API start besides the daily one), which pushed the events an
/// administrator reads off the first page. Whether it still runs at all is
/// answered by <see cref="RetentionSweepStatus"/> and the readiness check.
/// </remarks>
public class AccountDeletionWorker : BackgroundService
{
    private static readonly TimeSpan OverdueAlarmThreshold = TimeSpan.FromHours(24);
    private const int SweepReapplyBatchSize = 50;

    private static readonly JsonSerializerOptions SweepJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AccountDeletionSettings> _settings;
    private readonly RetentionSweepStatus _status;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountDeletionWorker> _logger;

    // Default(DateOnly) is far in the past, so the first cycle always sweeps —
    // the startup catch-up the restore runbook depends on.
    private DateOnly _lastSweepDateUtc;

    // A failed sweep is retried on every poll; only the first failure of a run
    // of failures gets an audit row, so an outage does not refill the log.
    private bool _failureRecordedSinceLastSuccess;

    public AccountDeletionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AccountDeletionSettings> settings,
        RetentionSweepStatus status,
        TimeProvider timeProvider,
        ILogger<AccountDeletionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _status = status;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Account deletion worker started (poll {PollMinutes}m, batch {BatchSize}, max attempts {MaxAttempts}).",
            _settings.CurrentValue.WorkerPollInterval.TotalMinutes, _settings.CurrentValue.WorkerBatchSize, _settings.CurrentValue.MaxExecutionAttempts);

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
                await Task.Delay(_settings.CurrentValue.WorkerPollInterval, _timeProvider, stoppingToken);
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
            UtcNow, _settings.CurrentValue.WorkerBatchSize, cancellationToken);

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
            UtcNow - OverdueAlarmThreshold, 1, cancellationToken);
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
        var todayUtc = DateOnly.FromDateTime(UtcNow);
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
    /// <remarks>
    /// Writes one audit row when it deleted anything, carrying every count, and
    /// nothing when it deleted nothing. A failure writes a failure row with the
    /// counts reached so far: always when something was already deleted, and
    /// otherwise only for the first failure since the last success.
    /// </remarks>
    public async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var auditLogRepository = services.GetRequiredService<IAuditLogRepository>();
        var settings = _settings.CurrentValue;

        // Whole UTC days, not "now": every run on the same day shares one cutoff,
        // so a run after an API restart finds nothing the day's first run left,
        // deletes nothing and writes nothing. A row outlives its period by less
        // than a day, which the daily cadence already allowed.
        var midnightUtc = UtcNow.Date;
        var tally = new SweepTally(midnightUtc.AddDays(-settings.EffectiveAuditLogRetentionDays));

        try
        {
            await PurgeAsync(services, settings, midnightUtc, tally, cancellationToken);

            tally.Step = SweepSteps.Record;
            if (tally.Total > 0)
            {
                await auditLogRepository.CreateAsync(
                    AuditLog.CreateSuccess(
                        actionType: AuditActionTypes.System,
                        action: AuditActions.SystemRetentionSweep,
                        userId: WellKnownUserIds.System,
                        additionalData: tally.ToJson(failedStep: null)),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await RecordFailedSweepAsync(auditLogRepository, tally, ex);
            throw;
        }

        _failureRecordedSinceLastSuccess = false;
        _status.RecordSuccess(UtcNow);

        _logger.LogInformation(
            "Retention sweep complete: {Reapplied} re-applied destructions, {DeletionCodes} deletion codes, {SecretChallenges} secret challenges, {LoginAttempts} login attempts, {PurgedOutbox} delivered outbox rows and {AuditLogs} audit rows (before {AuditCutoff:u}) purged, {Released} identifier reservations released",
            tally.ReappliedDeletions, tally.PurgedDeletionCodes, tally.PurgedSecretChallenges, tally.PurgedLoginAttempts,
            tally.PurgedOutboxRows, tally.PurgedAuditLogs, tally.AuditCutoffUtc, tally.ReleasedReservations);
    }

    private async Task PurgeAsync(
        IServiceProvider services,
        AccountDeletionSettings settings,
        DateTime midnightUtc,
        SweepTally tally,
        CancellationToken cancellationToken)
    {
        // 1) Restore re-apply: a Completed request with a live user row means
        //    a backup restore resurrected destroyed data (R5).
        tally.Step = SweepSteps.RestoreReapply;
        await ReapplyRestoredDeletionsAsync(services, tally, cancellationToken);

        // 2) Expired/used deletion OTPs (short-lived Class A rows).
        tally.Step = SweepSteps.DeletionCodes;
        tally.PurgedDeletionCodes = await services
            .GetRequiredService<IAccountDeletionVerificationRepository>()
            .DeleteExpiredAsync(cancellationToken);

        // 2b) Dead secret step-up challenges. Same class of row as the deletion
        //     OTPs above: a short-lived credential with no purpose once it can
        //     neither be answered nor spent. Without this the table only grew —
        //     its own definition promised this sweep and nothing performed it.
        tally.Step = SweepSteps.SecretChallenges;
        tally.PurgedSecretChallenges = await services
            .GetRequiredService<ISecretOperationChallengeRepository>()
            .DeleteExpiredAsync(cancellationToken);

        // 3) Anonymized login attempts past the fraud-analysis retention.
        tally.Step = SweepSteps.LoginAttempts;
        tally.PurgedLoginAttempts = await services
            .GetRequiredService<ILoginAttemptRepository>()
            .CleanupOldAttemptsAsync(midnightUtc.AddDays(-settings.EffectiveLoginAttemptRetentionDays), cancellationToken);

        // 4) Delivered mail past its retention (rendered recipient PII).
        tally.Step = SweepSteps.DeliveredMail;
        tally.PurgedOutboxRows = await services
            .GetRequiredService<INotificationOutboxRepository>()
            .DeleteSentOlderThanAsync(midnightUtc.AddDays(-settings.EffectiveOutboxRetentionDays), cancellationToken);

        // 5) Audit history past its retention. Runs after the other purges and
        //    before the sweep's own row, which is never inside the window it
        //    just purged. Without this call the table had no end of life at
        //    all: the published policy states a three-year floor and never a
        //    ceiling, and an identifier released after a finite reservation
        //    window could still be linked through audit rows that outlive it.
        tally.Step = SweepSteps.AuditLogs;
        tally.PurgedAuditLogs = await services
            .GetRequiredService<IAuditLogRepository>()
            .CleanupOldLogsAsync(tally.AuditCutoffUtc, cancellationToken);

        // 6) Identifier reservations whose window has elapsed. Deleting the row
        //    IS the erasure of a keyed digest that no key destruction can reach
        //    while live reservations still need that key. EffectiveIdentifier-
        //    ReservationDays is never shorter than the audit retention, so an
        //    address is never released while records keyed to it survive.
        tally.Step = SweepSteps.IdentifierReservations;
        tally.ReleasedReservations = await services
            .GetRequiredService<IAccountDeletionTombstoneRepository>()
            .DeleteExpiredAsync(midnightUtc.AddDays(-settings.EffectiveIdentifierReservationDays), cancellationToken);
    }

    private async Task ReapplyRestoredDeletionsAsync(
        IServiceProvider services,
        SweepTally tally,
        CancellationToken cancellationToken)
    {
        var requestRepository = services.GetRequiredService<IAccountDeletionRequestRepository>();
        var userRepository = services.GetRequiredService<IUserRepository>();
        var credentialRevocation = services.GetRequiredService<ICredentialRevocationService>();
        var auditLogRepository = services.GetRequiredService<IAuditLogRepository>();

        var resurrected = await requestRepository.GetCompletedWithLiveUserAsync(
            SweepReapplyBatchSize, cancellationToken);
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
                    tally.ReappliedDeletions++;
                    await auditLogRepository.CreateAsync(
                        AuditLog.CreateSuccess(
                            actionType: AuditActionTypes.UserManagement,
                            action: AuditActions.UserDeletionReapplied,
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
    }

    /// <summary>
    /// Records a sweep that stopped part-way. What was already deleted is
    /// recorded whatever stopped the run, because the row is the only trace of
    /// that destruction. A failure that deleted nothing is recorded once per run
    /// of failures: an outage retried every poll would otherwise write close to
    /// a hundred rows a day, the noise this sweep stopped writing. The log and
    /// the readiness check carry the rest.
    /// </summary>
    private async Task RecordFailedSweepAsync(
        IAuditLogRepository auditLogRepository,
        SweepTally tally,
        Exception exception)
    {
        var interrupted = exception is OperationCanceledException;
        var mustRecord = tally.Total > 0 || (!interrupted && !_failureRecordedSinceLastSuccess);
        if (!mustRecord)
        {
            return;
        }

        try
        {
            // CancellationToken.None: on shutdown the sweep's token is already
            // cancelled, and this row must still be written.
            await auditLogRepository.CreateAsync(
                AuditLog.CreateFailure(
                    actionType: AuditActionTypes.System,
                    action: AuditActions.SystemRetentionSweep,
                    // The step, never the exception text: that goes to the log,
                    // and the audit page is read by every platform administrator.
                    errorMessage: interrupted
                        ? $"Retention sweep interrupted during {tally.Step}."
                        : $"Retention sweep failed during {tally.Step}.",
                    userId: WellKnownUserIds.System,
                    additionalData: tally.ToJson(failedStep: tally.Step)),
                CancellationToken.None);

            if (!interrupted)
            {
                _failureRecordedSinceLastSuccess = true;
            }
        }
        catch (Exception recordException)
        {
            _logger.LogError(recordException,
                "Could not record the failed retention sweep (step {Step}); {Deleted} rows had been deleted before it stopped",
                tally.Step, tally.Total);
        }
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

    /// <summary>The step names a failure row reports in <c>failedStep</c>.</summary>
    private static class SweepSteps
    {
        public const string RestoreReapply = "restore-reapply";
        public const string DeletionCodes = "deletion-codes";
        public const string SecretChallenges = "secret-challenges";
        public const string LoginAttempts = "login-attempts";
        public const string DeliveredMail = "delivered-mail";
        public const string AuditLogs = "audit-logs";
        public const string IdentifierReservations = "identifier-reservations";
        public const string Record = "record";
    }

    /// <summary>What one sweep has done so far, and the step it is on.</summary>
    private sealed class SweepTally(DateTime auditCutoffUtc)
    {
        public DateTime AuditCutoffUtc { get; } = auditCutoffUtc;

        public string Step { get; set; } = SweepSteps.RestoreReapply;

        public int ReappliedDeletions { get; set; }
        public int PurgedDeletionCodes { get; set; }
        public int PurgedSecretChallenges { get; set; }
        public int PurgedLoginAttempts { get; set; }
        public int PurgedOutboxRows { get; set; }
        public int PurgedAuditLogs { get; set; }
        public int ReleasedReservations { get; set; }

        public long Total =>
            (long)ReappliedDeletions + PurgedDeletionCodes + PurgedSecretChallenges + PurgedLoginAttempts
            + PurgedOutboxRows + PurgedAuditLogs + ReleasedReservations;

        // The first five names are the ones the row carried before; kept so a
        // reader of older rows finds the same keys.
        public string ToJson(string? failedStep) => JsonSerializer.Serialize(
            new
            {
                reappliedDeletions = ReappliedDeletions,
                purgedOutboxRows = PurgedOutboxRows,
                auditCutoffUtc = AuditCutoffUtc,
                purgedSecretChallenges = PurgedSecretChallenges,
                releasedReservations = ReleasedReservations,
                purgedDeletionCodes = PurgedDeletionCodes,
                purgedLoginAttempts = PurgedLoginAttempts,
                purgedAuditLogs = PurgedAuditLogs,
                failedStep
            },
            SweepJsonOptions);
    }
}
