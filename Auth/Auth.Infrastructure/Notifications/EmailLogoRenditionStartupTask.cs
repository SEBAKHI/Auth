using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// One-shot startup backfill for the email-safe logo renditions.
/// </summary>
/// <remarks>
/// Renditions are normally built when an admin saves Platform settings, but a deployment that
/// introduces them finds every existing install with logos already configured and no rendition
/// on disk. Without this, those installs stay broken until somebody happens to re-save the
/// branding form — so the fix would silently not apply to the very environments that reported
/// the bug. Running here also re-plates after a storage volume is restored or the plate palette
/// changes. Never blocks startup: on failure the layout falls back to the text wordmark.
/// </remarks>
public class EmailLogoRenditionStartupTask : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailLogoRenditionStartupTask> _logger;

    public EmailLogoRenditionStartupTask(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailLogoRenditionStartupTask> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var platformSettingsRepository =
                scope.ServiceProvider.GetRequiredService<IPlatformSettingsRepository>();
            var imageStorage = scope.ServiceProvider.GetRequiredService<IImageStorageService>();

            var settings = await platformSettingsRepository.GetAsync(cancellationToken);
            if (settings is null)
            {
                return;
            }

            var light = await imageStorage.EnsureEmailLogoRenditionAsync(
                settings.LogoUrl, EmailLogoVariant.Light, cancellationToken);
            var dark = await imageStorage.EnsureEmailLogoRenditionAsync(
                settings.LogoUrlDark, EmailLogoVariant.Dark, cancellationToken);

            if (light is null && !string.IsNullOrWhiteSpace(settings.LogoUrl))
            {
                _logger.LogWarning(
                    "The platform logo has no email rendition, so emails will show the platform " +
                    "name as text instead. Re-upload the logo in Platform settings.");
            }
            else
            {
                _logger.LogInformation(
                    "Email logo renditions ready (light: {Light}, dark: {Dark}).",
                    light?.Key ?? "none", dark?.Key ?? "none");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email logo rendition startup backfill could not run.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
