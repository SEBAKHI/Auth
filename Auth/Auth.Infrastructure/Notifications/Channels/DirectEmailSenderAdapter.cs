using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Notifications.Channels;

/// <summary>
/// Exposes the raw SMTP transport to the Application layer for the
/// system-settings test-email diagnostic.
/// </summary>
public class DirectEmailSenderAdapter : IDirectEmailSender
{
    private readonly SmtpEmailSender _sender;

    public DirectEmailSenderAdapter(SmtpEmailSender sender)
    {
        _sender = sender;
    }

    /// <inheritdoc />
    public Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken cancellationToken)
        => _sender.SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
}
