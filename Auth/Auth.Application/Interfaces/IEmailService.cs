namespace Auth.Application.Interfaces;

/// <summary>
/// Service for sending emails.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email verification OTP.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="recipientName">Recipient name.</param>
    /// <param name="otp">The 6-digit OTP code.</param>
    /// <param name="expirationMinutes">OTP expiration time in minutes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if email was sent successfully.</returns>
    Task<bool> SendVerificationOtpAsync(
        string toEmail,
        string recipientName,
        string otp,
        int expirationMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a generic email.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML email body.</param>
    /// <param name="textBody">Plain text email body (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if email was sent successfully.</returns>
    Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
