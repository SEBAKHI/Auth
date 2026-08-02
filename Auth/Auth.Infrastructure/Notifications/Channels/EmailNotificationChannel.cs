using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Notifications.Channels;

/// <summary>
/// Email delivery strategy. Preserves the development-mode behavior of the
/// legacy service: when Email.Enabled is false the rendered message is logged
/// instead of sent and the operation reports success.
/// </summary>
public class EmailNotificationChannel : INotificationChannel
{
    private readonly IOptionsMonitor<EmailSettings> _settings;
    private readonly SmtpEmailSender _sender;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(
        IOptionsMonitor<EmailSettings> settings,
        SmtpEmailSender sender,
        ILogger<EmailNotificationChannel> logger)
    {
        _settings = settings;
        _sender = sender;
        _logger = logger;
    }

    /// <inheritdoc />
    public NotificationChannelType Channel => NotificationChannelType.Email;

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> SendAsync(
        RenderedNotification notification,
        CancellationToken cancellationToken)
    {
        if (!_settings.CurrentValue.Enabled)
        {
            _logger.LogInformation(
                "Email sending disabled. Would have sent to {Email} [{Language}]: {Subject}",
                notification.RecipientAddress, notification.LanguageCode, notification.Subject);
            _logger.LogDebug(
                "Rendered email body for {Email}:\n{Body}",
                notification.RecipientAddress, notification.BodyText);
            return Result.Success;
        }

        var sent = await _sender.SendAsync(
            notification.RecipientAddress,
            notification.Subject,
            notification.BodyHtml,
            notification.BodyText,
            cancellationToken);

        return sent ? Result.Success : NotificationErrors.SendFailed;
    }
}
