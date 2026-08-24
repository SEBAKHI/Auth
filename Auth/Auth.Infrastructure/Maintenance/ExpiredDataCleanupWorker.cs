using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Maintenance;

/// <summary>
/// Removes rows that fell out of use, once per UTC day (pattern:
/// <c>AccountDeletionWorker</c>).
/// </summary>
/// <remarks>
/// Six tables accumulated without bound because every repository had a cleanup
/// method and none had a caller — written code that never ran. A seventh,
/// UserSessions, is stamped rather than emptied: its rows are history a user can
/// see, so the sweep corrects what they say instead of erasing them.
/// <para>
/// Like the other workers here, this depends on the application staying loaded.
/// On a host that unloads an idle app pool the sweep is deferred, not lost: the
/// next run takes whatever accumulated meanwhile.
/// </para>
/// </remarks>
public class ExpiredDataCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DataRetentionSettings> _settings;
    private readonly ILogger<ExpiredDataCleanupWorker> _logger;

    // Default(DateOnly) is far in the past, so the first cycle after a start
    // always sweeps rather than waiting for the next UTC midnight.
    private DateOnly _lastSweepDateUtc;

    public ExpiredDataCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DataRetentionSettings> settings,
        ILogger<ExpiredDataCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Expired-data cleanup worker started (poll {PollMinutes}m, batch {BatchSize}, ceiling {MaxRows} rows per table per run).",
            _settings.CurrentValue.WorkerPollMinutes,
            _settings.CurrentValue.EffectiveBatchSize,
            _settings.CurrentValue.EffectiveMaxRowsPerTablePerRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDailySweepIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expired-data cleanup cycle failed; retrying on the next poll");
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
    /// Runs the sweep once per UTC day.
    /// </summary>
    /// <remarks>
    /// The date advances once the pass completes, and a pass completes even when
    /// individual tables failed — each is caught and logged so one bad table
    /// cannot stop the other six, which means a table that failed waits until
    /// tomorrow rather than retrying on the next poll.
    /// <para>
    /// Only a failure OUTSIDE the per-table loop — a scope that cannot be built,
    /// a database that cannot be reached at all — leaves the date untouched, and
    /// that one is retried on the next poll.
    /// </para>
    /// </remarks>
    public async Task RunDailySweepIfDueAsync(CancellationToken cancellationToken)
    {
        if (!_settings.CurrentValue.Enabled)
        {
            return;
        }

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (todayUtc == _lastSweepDateUtc)
        {
            return;
        }

        await RunSweepAsync(cancellationToken);
        _lastSweepDateUtc = todayUtc;
    }

    /// <summary>
    /// One pass over every table. Public so it can be driven directly by tests
    /// and by an operator-triggered run without waiting for the schedule.
    /// </summary>
    /// <remarks>
    /// Each table is independent: one that throws is logged and the rest still
    /// run. A single failing table must not leave the other six growing.
    /// <para>
    /// Cutoffs are computed here, once, from the application clock — so the whole
    /// run is evaluated against one instant, rather than each statement reading
    /// the database server's clock whenever it happens to execute.
    /// </para>
    /// </remarks>
    public async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var nowUtc = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // Smallest table first, so the cheap wins land even if a run is cut
        // short; refresh tokens, far the largest, go last.
        var steps = new (string Table, Func<DateTime, int, CancellationToken, Task<int>> Sweep, int Days)[]
        {
            ("AuthorizationCodes",
                sp.GetRequiredService<IAuthorizationCodeRepository>().CleanupExpiredAsync,
                settings.EffectiveAuthorizationCodeDays),
            ("TwoFactorChallenges",
                sp.GetRequiredService<ITwoFactorChallengeRepository>().CleanupExpiredAsync,
                settings.EffectiveTwoFactorChallengeDays),
            ("PasswordResetTokens",
                sp.GetRequiredService<IPasswordResetTokenRepository>().CleanupExpiredAsync,
                settings.EffectivePasswordResetTokenDays),
            ("EmailVerificationTokens",
                sp.GetRequiredService<IEmailVerificationTokenRepository>().CleanupExpiredAsync,
                settings.EffectiveEmailVerificationTokenDays),
            ("IdpSessions",
                sp.GetRequiredService<IIdpSessionRepository>().CleanupExpiredAsync,
                settings.EffectiveIdpSessionDays),
            ("RefreshTokens",
                sp.GetRequiredService<IRefreshTokenRepository>().CleanupExpiredAsync,
                settings.EffectiveRefreshTokenDays),
        };

        foreach (var step in steps)
        {
            await SweepTableAsync(
                step.Table, step.Sweep, nowUtc.AddDays(-step.Days), settings, cancellationToken);
        }

        await StampExpiredSessionsAsync(sp, settings, cancellationToken);
        await ReclaimAbandonedUploadsAsync(sp, nowUtc, cancellationToken);
    }

    /// <summary>
    /// Deletes the files behind uploads that were never attached to anything.
    /// </summary>
    /// <remarks>
    /// Uploading and attaching are separate calls, so a file whose upload
    /// succeeded and whose form was then abandoned stayed on disk forever with
    /// nothing referencing it and nothing looking for it. Not a table sweep like
    /// the steps above: rows and files have to go together, and the files are the
    /// point.
    ///
    /// Deliberately outside the per-run row ceiling those steps observe. The
    /// count here is bounded by how many uploads were abandoned since the last
    /// run, which is small; capping it would leave the remainder on disk until a
    /// later run that has no reason to be less busy.
    /// </remarks>
    private async Task ReclaimAbandonedUploadsAsync(
        IServiceProvider sp, DateTime nowUtc, CancellationToken cancellationToken)
    {
        try
        {
            // Resolution inside the guard, not above it. The six sweeps in this
            // run have already committed by now, and a service this step cannot
            // resolve is still no reason to fail the run — which is what the
            // catch below exists to say, and what it could not do while the first
            // resolution sat outside it.
            var imageSettings = sp.GetRequiredService<IOptionsMonitor<ImageStorageSettings>>().CurrentValue;
            var retentionHours = Math.Max(1, imageSettings.OrphanRetentionHours);

            var uploads = sp.GetRequiredService<IUploadedImageRepository>();
            var storage = sp.GetRequiredService<IImageStorageService>();

            var reclaimed = await uploads.ReclaimUnattachedAsync(
                nowUtc.AddHours(-retentionHours), cancellationToken);

            foreach (var key in reclaimed)
            {
                await storage.DeleteImageAsync(key, cancellationToken);
            }

            if (reclaimed.Count > 0)
            {
                _logger.LogInformation(
                    "Reclaimed {Count} abandoned uploads older than {Hours}h", reclaimed.Count, retentionHours);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Housekeeping must never take the worker down: the other sweeps in
            // this run have already committed, and disk space is the least urgent
            // thing this process is responsible for.
            _logger.LogError(ex, "Failed to reclaim abandoned uploads");
        }
    }

    /// <summary>
    /// Drains one table in batches until nothing eligible is left, the per-run
    /// ceiling is reached, or shutdown is requested.
    /// </summary>
    private async Task SweepTableAsync(
        string table,
        Func<DateTime, int, CancellationToken, Task<int>> sweep,
        DateTime cutoffUtc,
        DataRetentionSettings settings,
        CancellationToken cancellationToken)
    {
        var batchSize = settings.EffectiveBatchSize;
        var ceiling = settings.EffectiveMaxRowsPerTablePerRun;
        var removed = 0;

        try
        {
            while (removed < ceiling && !cancellationToken.IsCancellationRequested)
            {
                var deleted = await sweep(cutoffUtc, batchSize, cancellationToken);
                removed += deleted;

                // A short batch means the table has nothing left to give.
                if (deleted < batchSize)
                {
                    break;
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation(
                    "Retention sweep removed {RowCount} rows from {Table} older than {CutoffUtc:u}. Ceiling reached: {CeilingReached}",
                    removed, table, cutoffUtc, removed >= ceiling);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Swallowed per table on purpose: the remaining tables must still be
            // swept. The sweep is idempotent, so the next run retries this one.
            _logger.LogError(ex,
                "Retention sweep failed for {Table} after removing {RowCount} rows; the other tables continue",
                table, removed);
        }
    }

    private async Task StampExpiredSessionsAsync(
        IServiceProvider sp, DataRetentionSettings settings, CancellationToken cancellationToken)
    {
        var batchSize = settings.EffectiveBatchSize;
        var ceiling = settings.EffectiveMaxRowsPerTablePerRun;
        var stamped = 0;

        try
        {
            var repository = sp.GetRequiredService<IUserSessionRepository>();

            while (stamped < ceiling && !cancellationToken.IsCancellationRequested)
            {
                var rows = await repository.MarkExpiredSessionsEndedAsync(batchSize, cancellationToken);
                stamped += rows;

                if (rows < batchSize)
                {
                    break;
                }
            }

            if (stamped > 0)
            {
                _logger.LogInformation(
                    "Retention sweep stamped {RowCount} expired sessions as ended", stamped);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Stamping expired sessions failed after {RowCount} rows; the deletions in this run stand", stamped);
        }
    }
}
