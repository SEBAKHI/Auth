using Auth.Application.SystemSettings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Configuration;

/// <summary>
/// Safety-net refresh of the database settings layer. Saves through the API
/// reload in-process directly; this timer only bounds staleness after
/// out-of-band SQL edits, another instance's writes, or a database outage
/// at startup (fail-open leaves file values active until the next tick).
/// </summary>
public sealed class SystemSettingsRefreshService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ISystemSettingsReloader _reloader;
    private readonly ILogger<SystemSettingsRefreshService> _logger;

    public SystemSettingsRefreshService(
        ISystemSettingsReloader reloader,
        ILogger<SystemSettingsRefreshService> logger)
    {
        _reloader = reloader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _reloader.Reload();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Periodic system-settings refresh failed; keeping last known values");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
