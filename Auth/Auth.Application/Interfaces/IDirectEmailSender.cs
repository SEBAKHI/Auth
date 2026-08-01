namespace Auth.Application.Interfaces;

/// <summary>
/// Sends a single raw email through the configured SMTP transport, bypassing
/// the notification template pipeline. Used only for administrative
/// diagnostics (the system-settings "send test email" action).
/// </summary>
public interface IDirectEmailSender
{
    /// <summary>
    /// Sends one email. Returns false (never throws) on failure.
    /// </summary>
    Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken cancellationToken);
}
