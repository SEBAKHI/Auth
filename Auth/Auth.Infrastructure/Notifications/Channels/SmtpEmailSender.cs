using Auth.Application.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Auth.Infrastructure.Notifications.Channels;

/// <summary>
/// Raw MailKit SMTP transport (extracted unchanged from the legacy email
/// service): one connection per message, 30s timeout, and the port-465
/// implicit-TLS mapping required by the production host.
/// </summary>
public class SmtpEmailSender
{
    private const int SendTimeoutMilliseconds = 30_000;

    private readonly IOptionsMonitor<EmailSettings> _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptionsMonitor<EmailSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Sends one email. Returns false (never throws) on failure.
    /// </summary>
    public async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.CurrentValue;
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (!string.IsNullOrEmpty(textBody))
            {
                bodyBuilder.TextBody = textBody;
            }
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient { Timeout = SendTimeoutMilliseconds };
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, GetSocketOptions(settings), cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                // A configured username with no password is a misconfiguration, not a transient
                // failure. MailKit rejects a null password, and that exception would be swallowed
                // by the catch below and reported as a generic "Failed to send email".
                if (string.IsNullOrEmpty(settings.Password))
                {
                    _logger.LogError(
                        "SMTP is misconfigured: username '{Username}' is set but no password is configured. " +
                        "Set the 'Email:Password' configuration key. Email to {Email} was not sent.",
                        settings.Username,
                        toEmail);
                    return false;
                }

                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email sent successfully to {Email}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
            return false;
        }
    }

    /// <summary>
    /// Maps the configured port and SSL flag to the MailKit connection security mode.
    /// Port 465 uses implicit TLS (the server expects a TLS handshake immediately on connect);
    /// other ports use STARTTLS — required when <see cref="EmailSettings.UseSsl"/> is true,
    /// opportunistic otherwise (e.g. local development SMTP servers without TLS).
    /// </summary>
    private static SecureSocketOptions GetSocketOptions(EmailSettings settings) =>
        settings.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : settings.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;
}
