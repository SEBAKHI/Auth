using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Notifications.Channels;

/// <summary>
/// Email delivery strategy. Preserves the development-mode behavior of the
/// legacy service: when Email.Enabled is false the message is logged instead of
/// sent and the operation reports success. The rendered BODY is logged only in
/// Development, because it carries whatever secret the template embedded.
/// </summary>
/// <remarks>
/// One exception (S24, EO3): a type that is useless unless it really arrives
/// (<see cref="NotificationTypeCodes.DeliveryRequiredCodes"/>, the emailed
/// second-factor code) is refused while Email.Enabled is false — before the
/// Development body line, so its code is never logged — and its outbox row retries
/// and then ends Dead, redacted, instead of Sent with nothing sent.
/// </remarks>
public class EmailNotificationChannel : INotificationChannel
{
    private readonly IOptionsMonitor<EmailSettings> _settings;
    private readonly SmtpEmailSender _sender;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(
        IOptionsMonitor<EmailSettings> settings,
        SmtpEmailSender sender,
        IEnvironmentInfo environment,
        ILogger<EmailNotificationChannel> logger)
    {
        _settings = settings;
        _sender = sender;
        _environment = environment;
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
            if (notification.NotificationTypeCode is { } typeCode
                && NotificationTypeCodes.DeliveryRequiredCodes.Contains(typeCode))
            {
                _logger.LogWarning(
                    "Email sending disabled. Refused {TypeCode} to {Email}: it is useless unless delivered",
                    typeCode, EmailMasking.Mask(notification.RecipientAddress));
                return NotificationErrors.SendFailed;
            }

            _logger.LogInformation(
                "Email sending disabled. Would have sent to {Email} [{Language}]: {Subject}",
                EmailMasking.Mask(notification.RecipientAddress), notification.LanguageCode, notification.Subject);

            // The rendered body carries whatever the template embedded - a verification
            // code, a password-reset link, an ownership-transfer code. Gated on the
            // environment as well as the level, because both switches that would
            // otherwise expose it are hot: Email:Enabled and the Serilog minimum level
            // are each editable from the console in production, so on their own they
            // would dump every outgoing secret into the production log.
            if (_environment.IsDevelopment)
            {
                _logger.LogDebug(
                    "Rendered email body for {Email}:\n{Body}",
                    EmailMasking.Mask(notification.RecipientAddress), notification.BodyText);
            }

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
