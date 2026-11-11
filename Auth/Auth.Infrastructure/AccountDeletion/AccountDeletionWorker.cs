using System.Runtime.ExceptionServices;
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
/// The sweep writes to the audit log when it changed something, when it
/// failed, and, on runs with nothing to delete, at most once every 30 days as
/// proof that it still runs. It used to write a row on every run, including
/// every run after an API start, which pushed the events an administrator reads
/// off the first page. <see cref="RetentionSweepStatus"/> and the readiness
/// check report whether it ran in this process.
/// </remarks>
public class AccountDeletionWorker : BackgroundService
{
    private static readonly TimeSpan OverdueAlarmThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// The longest gap between two sweep rows on a system where the sweep finds
    /// nothing to delete (owner decision D-161-1): a durable proof that the
    /// periodic destruction still runs, well inside its six-month interval.
    /// </summary>
    internal static readonly TimeSpan ProofRowInterval = TimeSpan.FromDays(30);

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

    // The UTC day of the last failure row, cleared by a success. A sweep that
    // fails on every retry writes at most one failure row a day: enough to stay
    // visible through restarts and recycles, never a row per retry.
    private DateOnly? _failureRecordedOnUtcDay;

    // Counts of rows deleted by a run whose row could not be written either.
    // Added to the next run's counts, so a purge is never left without its row
    // while this process lives.
    private SweepTally? _unrecorded;

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
            _settings.CurrentValue.EffectiveWorkerPollMinutes, _settings.CurrentValue.EffectiveWorkerBatchSize,
            _settings.CurrentValue.EffectiveMaxExecutionAttempts);

        await SafeReclaimAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteDuePassAsync(stoppingToken);
                await RunDailySweepIfDueAsync(stoppingToken);
            }
            // Only the host's own stop ends the loop. Any other cancellation (a
            // timeout surfacing as TaskCanceledException) is a failed cycle,
            // logged and retried; it used to end the loop with no log line,
            // which stopped deletions and the sweep for the life of the process.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Account deletion worker stopped.");
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
            UtcNow, _settings.CurrentValue.EffectiveWorkerBatchSize, cancellationToken);

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
    /// <para>
    /// Every step runs whatever happened to the steps before it; only the
    /// host's own stop ends a run early. A run in which any step failed counts
    /// as failed: it writes a failure row naming the steps and carrying every
    /// count, and is retried on the next poll.
    /// </para>
    /// <para>
    /// A successful run writes one row when it deleted anything. When it
    /// deleted nothing, it writes one only if the newest sweep row is more than
    /// <see cref="ProofRowInterval"/> old.
    /// </para>
    /// </remarks>
    public async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var auditLogRepository = services.GetRequiredService<IAuditLogRepository>();
        var settings = _settings.CurrentValue;

        // Whole UTC days, not "now": every run on one day shares one cutoff, so
        // a run after an API restart finds nothing on the cutoff-based steps
        // that the day's first run left. Deletion codes and secret challenges
        // expire against the database clock instead, so a restart can still
        // delete some of those and write a row. A row can outlive its period by
        // up to one day plus one poll, the bound the daily cadence already had.
        var midnightUtc = UtcNow.Date;
        var tally = new SweepTally(midnightUtc.AddDays(-settings.EffectiveAuditLogRetentionDays));
        // Counts no row has recorded yet start this run's counts, so every step
        // below adds to its count and never assigns it.
        tally.AddCounts(_unrecorded);

        var failures = new List<Exception>();
        try
        {
            await PurgeAsync(services, settings, midnightUtc, tally, failures, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            await RecordFailedSweepAsync(auditLogRepository, tally, ex, cancellationToken);
            throw;
        }

        if (failures.Count > 0)
        {
            await RecordFailedSweepAsync(auditLogRepository, tally, failures[0], cancellationToken);
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            throw new AggregateException("The retention sweep failed at more than one step.", failures);
        }

        tally.Step = SweepSteps.Record;
        try
        {
            if (tally.HasChanges || await ProofRowDueAsync(auditLogRepository, cancellationToken))
            {
                await auditLogRepository.CreateAsync(
                    AuditLog.CreateSuccess(
                        actionType: AuditActionTypes.System,
                        action: AuditActions.SystemRetentionSweep,
                        userId: WellKnownUserIds.System,
                        additionalData: tally.ToJson(failedSteps: null)),
                    cancellationToken);
                _unrecorded = null;
            }
        }
        catch (Exception ex)
        {
            tally.FailedSteps.Add(SweepSteps.Record);
            await RecordFailedSweepAsync(auditLogRepository, tally, ex, cancellationToken);
            throw;
        }

        _failureRecordedOnUtcDay = null;
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
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        // A step that fails on every run (a delete that keeps timing out) must
        // not stop the purges after it, or every later retention period stops
        // being enforced. The host's own stop is not a step failure: it ends
        // the run here and is recorded as an interruption.
        async Task StepAsync(string step, Func<Task> run)
        {
            tally.Step = step;
            try
            {
                await run();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                tally.FailedSteps.Add(step);
                failures.Add(ex);
                _logger.LogWarning(ex, "Retention sweep step {Step} failed; the sweep goes on with the next step", step);
            }
        }

        // 1) Restore re-apply: a Completed request with a live user row means
        //    a backup restore resurrected destroyed data (R5).
        await StepAsync(SweepSteps.RestoreReapply,
            () => ReapplyRestoredDeletionsAsync(services, tally, cancellationToken));

        // 2) Expired/used deletion OTPs (short-lived Class A rows).
        await StepAsync(SweepSteps.DeletionCodes, async () =>
            tally.PurgedDeletionCodes += await services
                .GetRequiredService<IAccountDeletionVerificationRepository>()
                .DeleteExpiredAsync(cancellationToken));

        // 2b) Dead secret step-up challenges. Same class of row as the deletion
        //     OTPs above: a short-lived credential with no purpose once it can
        //     neither be answered nor spent. Without this the table only grew —
        //     its own definition promised this sweep and nothing performed it.
        await StepAsync(SweepSteps.SecretChallenges, async () =>
            tally.PurgedSecretChallenges += await services
                .GetRequiredService<ISecretOperationChallengeRepository>()
                .DeleteExpiredAsync(cancellationToken));

        // 3) Anonymized login attempts past the fraud-analysis retention.
        await StepAsync(SweepSteps.LoginAttempts, async () =>
            tally.PurgedLoginAttempts += await services
                .GetRequiredService<ILoginAttemptRepository>()
                .CleanupOldAttemptsAsync(midnightUtc.AddDays(-settings.EffectiveLoginAttemptRetentionDays), cancellationToken));

        // 4) Delivered mail past its retention (rendered recipient PII).
        await StepAsync(SweepSteps.DeliveredMail, async () =>
            tally.PurgedOutboxRows += await services
                .GetRequiredService<INotificationOutboxRepository>()
                .DeleteSentOlderThanAsync(midnightUtc.AddDays(-settings.EffectiveOutboxRetentionDays), cancellationToken));

        // 5) Audit history past its retention. Runs after the other purges and
        //    before the sweep's own row, which is never inside the window it
        //    just purged. Without this call the table had no end of life at
        //    all: the published policy states a three-year floor and never a
        //    ceiling, and an identifier released after a finite reservation
        //    window could still be linked through audit rows that outlive it.
        await StepAsync(SweepSteps.AuditLogs, async () =>
            tally.PurgedAuditLogs += await services
                .GetRequiredService<IAuditLogRepository>()
                .CleanupOldLogsAsync(tally.AuditCutoffUtc, cancellationToken));

        // 6) Identifier reservations whose window has elapsed. Deleting the row
        //    IS the erasure of a keyed digest that no key destruction can reach
        //    while live reservations still need that key. EffectiveIdentifier-
        //    ReservationDays is never shorter than the audit retention, so an
        //    address is never released while records keyed to it survive.
        await StepAsync(SweepSteps.IdentifierReservations, async () =>
            tally.ReleasedReservations += await services
                .GetRequiredService<IAccountDeletionTombstoneRepository>()
                .DeleteExpiredAsync(midnightUtc.AddDays(-settings.EffectiveIdentifierReservationDays), cancellationToken));
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
    /// Whether a run that deleted nothing writes its row (owner decision
    /// D-161-1): only when the newest sweep row of any outcome is older than
    /// <see cref="ProofRowInterval"/>, or when there is none. One seek through
    /// <c>IX_AuditLogs_Action</c>. A lookup that fails is logged and answers
    /// no: the proof row is never worth failing the sweep for.
    /// </summary>
    private async Task<bool> ProofRowDueAsync(IAuditLogRepository auditLogRepository, CancellationToken cancellationToken)
    {
        try
        {
            var newest = await auditLogRepository.GetLatestTimestampAsync(
                AuditActions.SystemRetentionSweep, cancellationToken);
            return newest is null || UtcNow - newest.Value > ProofRowInterval;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the newest retention sweep row; no proof row this run");
            return false;
        }
    }

    /// <summary>
    /// Records a sweep that failed or stopped. What was already deleted is
    /// recorded whatever stopped the run. A failure that deleted nothing is
    /// recorded once per UTC day while it lasts; an interruption that deleted
    /// nothing is not recorded. When the row cannot be written either, its
    /// counts are carried into the next run's row.
    /// </summary>
    private async Task RecordFailedSweepAsync(
        IAuditLogRepository auditLogRepository,
        SweepTally tally,
        Exception exception,
        CancellationToken sweepToken)
    {
        var interrupted = exception is OperationCanceledException && sweepToken.IsCancellationRequested;
        var todayUtc = DateOnly.FromDateTime(UtcNow);
        var mustRecord = tally.HasChanges || (!interrupted && _failureRecordedOnUtcDay != todayUtc);
        if (!mustRecord)
        {
            return;
        }

        var steps = interrupted
            ? tally.FailedSteps.Append(tally.Step).Distinct().ToList()
            : tally.FailedSteps;

        try
        {
            // CancellationToken.None, never the sweep's token: on shutdown that
            // token is already cancelled, and this row must still be written.
            await auditLogRepository.CreateAsync(
                AuditLog.CreateFailure(
                    actionType: AuditActionTypes.System,
                    action: AuditActions.SystemRetentionSweep,
                    // The steps, never the exception text: that goes to the log,
                    // and the audit page is read by every platform administrator.
                    errorMessage: interrupted
                        ? $"Retention sweep interrupted during {tally.Step}."
                        : $"Retention sweep failed during {string.Join(", ", tally.FailedSteps)}.",
                    userId: WellKnownUserIds.System,
                    additionalData: tally.ToJson(steps)),
                CancellationToken.None);

            _unrecorded = null;
            if (!interrupted)
            {
                _failureRecordedOnUtcDay = todayUtc;
            }
        }
        catch (Exception recordException)
        {
            if (tally.HasChanges)
            {
                _unrecorded = tally;
            }

            _logger.LogError(recordException,
                "Could not record the failed retention sweep (steps {Steps}); {Deleted} rows deleted so far are carried into the next sweep's row",
                string.Join(", ", steps), tally.Total);
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

    /// <summary>The step names a failure row reports in <c>failedSteps</c>.</summary>
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

    /// <summary>What one sweep has done so far, the step it is on, and the steps that failed.</summary>
    private sealed class SweepTally(DateTime auditCutoffUtc)
    {
        public DateTime AuditCutoffUtc { get; } = auditCutoffUtc;

        public string Step { get; set; } = SweepSteps.RestoreReapply;

        public List<string> FailedSteps { get; } = [];

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

        // Any count other than zero, not "above zero": a server whose
        // connections run with NOCOUNT reports -1 rows for a statement, and
        // that must not read as "nothing happened".
        public bool HasChanges =>
            ReappliedDeletions != 0 || PurgedDeletionCodes != 0 || PurgedSecretChallenges != 0
            || PurgedLoginAttempts != 0 || PurgedOutboxRows != 0 || PurgedAuditLogs != 0
            || ReleasedReservations != 0;

        public void AddCounts(SweepTally? earlier)
        {
            if (earlier is null)
            {
                return;
            }

            ReappliedDeletions += earlier.ReappliedDeletions;
            PurgedDeletionCodes += earlier.PurgedDeletionCodes;
            PurgedSecretChallenges += earlier.PurgedSecretChallenges;
            PurgedLoginAttempts += earlier.PurgedLoginAttempts;
            PurgedOutboxRows += earlier.PurgedOutboxRows;
            PurgedAuditLogs += earlier.PurgedAuditLogs;
            ReleasedReservations += earlier.ReleasedReservations;
        }

        // The first five names are the ones the row carried before; kept so a
        // reader of older rows finds the same keys.
        public string ToJson(IReadOnlyList<string>? failedSteps) => JsonSerializer.Serialize(
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
                failedSteps
            },
            SweepJsonOptions);
    }
}
