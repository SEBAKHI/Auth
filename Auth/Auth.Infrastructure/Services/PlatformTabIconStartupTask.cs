using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Services;

/// <summary>
/// One-shot startup build of the tab icon (<see cref="PlatformTabIcon"/>).
/// </summary>
/// <remarks>
/// The icon is normally rebuilt when an administrator saves Platform settings, but every
/// install that predates it has its favicon uploaded and no file at the stable URL, and an
/// install that never saved its settings needs the default mark there. Without this, their
/// tabs would show no icon until somebody re-saves the branding. Running here also rebuilds it
/// after a storage volume is restored. Never blocks startup.
/// </remarks>
public class PlatformTabIconStartupTask : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformTabIconStartupTask> _logger;

    public PlatformTabIconStartupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<PlatformTabIconStartupTask> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = await scope.ServiceProvider
                .GetRequiredService<IPlatformSettingsRepository>()
                .GetAsync(cancellationToken);

            var built = await scope.ServiceProvider
                .GetRequiredService<PlatformTabIcon>()
                .RebuildAsync(settings ?? PlatformSettings.CreateDefault(), cancellationToken);

            if (built)
            {
                _logger.LogInformation("Tab icon ready.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tab icon startup build could not run.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
