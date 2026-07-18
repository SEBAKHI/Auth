using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// One-shot startup guard for the no-code-fallback design: the database is the
/// only template source, so a skipped or partial seed would break critical auth
/// emails. Logs an error listing every system type without a published global
/// Email template; the send path then fails loudly per send with TemplateNotPublished.
/// </summary>
public class NotificationTemplateStartupCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationTemplateStartupCheck> _logger;

    public NotificationTemplateStartupCheck(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationTemplateStartupCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<INotificationTemplateRepository>();

            var missing = await repository.GetSystemTypeCodesMissingPublishedGlobalTemplateAsync(
                NotificationChannelType.Email, cancellationToken);

            if (missing.Count > 0)
            {
                _logger.LogError(
                    "Notification template seed is incomplete: no published global Email template for system type(s) {TypeCodes}. " +
                    "Critical auth emails WILL FAIL until the templates are published (re-run the Auth_DB post-deployment seed or publish from the console).",
                    string.Join(", ", missing));
            }
            else
            {
                _logger.LogInformation("Notification template startup check passed: all system types have published global Email templates.");
            }
        }
        catch (Exception ex)
        {
            // The check must never block startup (e.g. DB briefly unavailable);
            // the send path still fails loudly per send.
            _logger.LogWarning(ex, "Notification template startup check could not run.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
