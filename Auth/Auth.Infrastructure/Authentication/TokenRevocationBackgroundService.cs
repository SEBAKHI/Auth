using System.Threading.Channels;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Backs the in-memory <see cref="TokenBlacklistService"/> with durable storage:
/// loads the cache from <c>RevokedTokens</c> before the server takes a request (so
/// revocations survive app-pool recycles), re-reads it periodically, drains the
/// write-behind queue to the store, and periodically purges expired rows.
/// </summary>
/// <remarks>
/// <para>
/// The first load runs in <see cref="StartAsync"/>, within <see cref="Schedule.LoadBudget"/>.
/// The host awaits every hosted service's start before it starts the server, so no
/// request is answered from an empty list unless the load failed. A failure is written
/// as Critical (<see cref="RevocationSignalCodes.RehydrationFailed"/>), keeps the readiness
/// check unhealthy, and is retried every <see cref="Schedule.RetryInterval"/>; the server
/// still starts, because refusing every bearer token while the database is away is a
/// product choice this service does not make.
/// </para>
/// <para>
/// After the first load, the whole list is read again every
/// <see cref="Schedule.RereadInterval"/> and merged (entries are added or extended, never
/// removed). That is how a revocation written by another process (the old worker of an
/// overlapped recycle, or the break-glass script) reaches this one.
/// </para>
/// <para>
/// At shutdown, what is still queued is written under a budget of its own
/// (<see cref="Schedule.DrainBudget"/>), not the stopping token, which is already
/// cancelled by then; what does not make it is counted in one Critical line.
/// </para>
/// </remarks>
public class TokenRevocationBackgroundService : BackgroundService
{
    /// <summary>The timings of the service; only tests shorten them.</summary>
    /// <param name="LoadBudget">The longest one load may take, the first one included (it holds the start of the server).</param>
    /// <param name="RetryInterval">The wait between failed loads, until one succeeds.</param>
    /// <param name="RereadInterval">The wait between full re-reads once the list is loaded.</param>
    /// <param name="PurgeInterval">The wait between purges of rows no longer in force.</param>
    /// <param name="DrainBudget">The longest the shutdown drain may take.</param>
    internal sealed record Schedule(
        TimeSpan LoadBudget,
        TimeSpan RetryInterval,
        TimeSpan RereadInterval,
        TimeSpan PurgeInterval,
        TimeSpan DrainBudget)
    {
        public static Schedule Default { get; } = new(
            LoadBudget: TimeSpan.FromSeconds(20),
            RetryInterval: TimeSpan.FromSeconds(30),
            RereadInterval: TimeSpan.FromMinutes(5),
            PurgeInterval: TimeSpan.FromMinutes(15),
            DrainBudget: TimeSpan.FromSeconds(5));
    }

    private readonly ChannelReader<TokenRevocation> _queue;
    private readonly TokenBlacklistService _blacklist;
    private readonly RevocationRehydrationStatus _status;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenRevocationBackgroundService> _logger;
    private readonly Schedule _schedule;

    public TokenRevocationBackgroundService(
        ChannelReader<TokenRevocation> queue,
        TokenBlacklistService blacklist,
        RevocationRehydrationStatus status,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<TokenRevocationBackgroundService> logger)
        : this(queue, blacklist, status, scopeFactory, timeProvider, logger, Schedule.Default)
    {
    }

    internal TokenRevocationBackgroundService(
        ChannelReader<TokenRevocation> queue,
        TokenBlacklistService blacklist,
        RevocationRehydrationStatus status,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<TokenRevocationBackgroundService> logger,
        Schedule schedule)
    {
        _queue = queue;
        _blacklist = blacklist;
        _status = status;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _schedule = schedule;
    }

    /// <summary>
    /// Loads the list before the host goes on to start the server, then starts the
    /// background loops.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RehydrateAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshTask = RefreshLoopAsync(stoppingToken);
        var purgeTask = PurgeLoopAsync(stoppingToken);

        // A revocation whose write the stop interrupted goes first in the drain.
        TokenRevocation? interrupted = null;
        try
        {
            await foreach (var revocation in _queue.ReadAllAsync(stoppingToken))
            {
                if (!await PersistAsync(revocation, stoppingToken) && stoppingToken.IsCancellationRequested)
                {
                    interrupted = revocation;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: what is still queued is written below.
        }

        await DrainAsync(interrupted);
        await Task.WhenAll(refreshTask, purgeTask);
    }

    /// <summary>
    /// One load of the whole list, recorded either way. Returns whether it succeeded.
    /// </summary>
    private async Task<bool> RehydrateAsync(CancellationToken hostToken)
    {
        var attempt = _status.FailedAttempts + 1;
        var started = _timeProvider.GetTimestamp();
        try
        {
            var count = await LoadWithinBudgetAsync(hostToken);
            _status.RecordLoaded();
            _logger.LogInformation(
                "{EventCode}: loaded {Count} token revocations from durable storage in {ElapsedMs} ms (attempt {Attempt})",
                RevocationSignalCodes.Rehydrated, count, ElapsedMs(started), attempt);
            return true;
        }
        catch (Exception ex) when (!hostToken.IsCancellationRequested)
        {
            _status.RecordFailed();
            _logger.LogCritical(ex,
                "{EventCode}: could not load the token revocation list from durable storage in {ElapsedMs} ms (attempt {Attempt}): until a load succeeds, an access token revoked before this start is accepted again. Retrying every {RetrySeconds} s",
                RevocationSignalCodes.RehydrationFailed, ElapsedMs(started), attempt, _schedule.RetryInterval.TotalSeconds);
            return false;
        }
    }

    /// <summary>
    /// Reads and merges the whole list, within <see cref="Schedule.LoadBudget"/> whether or
    /// not the store honours the token: a load still running past it only merges late, which
    /// can add entries and never remove one.
    /// </summary>
    private async Task<int> LoadWithinBudgetAsync(CancellationToken hostToken)
    {
        using var budget = new CancellationTokenSource(_schedule.LoadBudget, _timeProvider);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(hostToken, budget.Token);
        var load = LoadAsync(attempt.Token);
        try
        {
            return await load.WaitAsync(attempt.Token);
        }
        catch
        {
            // The abandoned load's own failure, if it comes, is observed here and dropped:
            // the failure that counts is the one just caught.
            _ = load.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private async Task<int> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
        var active = await store.GetActiveAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return _blacklist.LoadSnapshot(active);
    }

    /// <summary>
    /// Retries a failed start every <see cref="Schedule.RetryInterval"/> until a load
    /// succeeds, then re-reads the whole list every <see cref="Schedule.RereadInterval"/>.
    /// </summary>
    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!_status.IsLoaded)
            {
                await Task.Delay(_schedule.RetryInterval, _timeProvider, stoppingToken);
                await RehydrateAsync(stoppingToken);
            }

            using var timer = new PeriodicTimer(_schedule.RereadInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var count = await LoadWithinBudgetAsync(stoppingToken);
                    _logger.LogDebug("Re-read {Count} token revocations from durable storage", count);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // The list in memory stays as it was; a revocation another process wrote
                    // since the last read arrives with the next one that succeeds.
                    _logger.LogError(ex, "Failed to re-read the token revocation list from durable storage");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>
    /// Writes what is still queued after the read loop ended, under a budget of its own.
    /// </summary>
    private async Task DrainAsync(TokenRevocation? interrupted)
    {
        using var budget = new CancellationTokenSource(_schedule.DrainBudget, _timeProvider);
        var unwritten = 0;

        if (interrupted is not null && !await PersistAsync(interrupted, budget.Token))
        {
            unwritten++;
        }

        while (_queue.TryRead(out var revocation))
        {
            if (budget.IsCancellationRequested || !await PersistAsync(revocation, budget.Token))
            {
                unwritten++;
            }
        }

        if (unwritten > 0)
        {
            _logger.LogCritical(
                "{Count} token revocations queued at shutdown were not written to durable storage: they held in this process only, so the next process accepts the tokens they revoked until those expire",
                unwritten);
        }
    }

    /// <summary>
    /// Writes one revocation. A failure while <paramref name="cancellationToken"/> is
    /// cancelled is not logged (the caller decides what it means); any other is.
    /// </summary>
    private async Task<bool> PersistAsync(TokenRevocation revocation, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
            await store.AddAsync(revocation, cancellationToken);
            return true;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist token revocation {Type}:{Key} to durable storage", revocation.Type, revocation.Key);
            return false;
        }
    }

    private async Task PurgeLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_schedule.PurgeInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
                    await store.PurgeExpiredAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Failed to purge expired token revocations");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private long ElapsedMs(long started) => (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds;
}
