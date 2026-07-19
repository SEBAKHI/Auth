using System.Threading.Channels;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Backs the in-memory <see cref="TokenBlacklistService"/> with durable storage:
/// rehydrates the cache from <c>RevokedTokens</c> on startup (so revocations
/// survive app-pool recycles), drains the write-behind queue to the store, and
/// periodically purges expired rows.
/// </summary>
public class TokenRevocationBackgroundService : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(15);

    private readonly ChannelReader<TokenRevocation> _queue;
    private readonly TokenBlacklistService _blacklist;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenRevocationBackgroundService> _logger;

    public TokenRevocationBackgroundService(
        ChannelReader<TokenRevocation> queue,
        TokenBlacklistService blacklist,
        IServiceScopeFactory scopeFactory,
        ILogger<TokenRevocationBackgroundService> logger)
    {
        _queue = queue;
        _blacklist = blacklist;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RehydrateAsync(stoppingToken);

        var purgeTask = PurgeLoopAsync(stoppingToken);

        try
        {
            await foreach (var revocation in _queue.ReadAllAsync(stoppingToken))
            {
                await PersistAsync(revocation, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }

        await purgeTask;
    }

    private async Task RehydrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
            var active = await store.GetActiveAsync(DateTime.UtcNow, cancellationToken);
            _blacklist.LoadSnapshot(active);
        }
        catch (Exception ex)
        {
            // Never block startup on rehydration: worst case the cache starts
            // empty (the previous behavior), and durable refresh-token
            // revocation still bounds any exposure.
            _logger.LogError(ex, "Failed to rehydrate the token blacklist from durable storage");
        }
    }

    private async Task PersistAsync(TokenRevocation revocation, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
            await store.AddAsync(revocation, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to persist token revocation {Type}:{Key} to durable storage", revocation.Type, revocation.Key);
        }
    }

    private async Task PurgeLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PurgeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IRevokedTokenStore>();
                    await store.PurgeExpiredAsync(DateTime.UtcNow, cancellationToken);
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
}
