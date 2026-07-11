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
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a password reset email containing a reset link and the reset token.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="recipientName">Recipient name.</param>
    /// <param name="resetToken">The plaintext password reset token.</param>
    /// <param name="expirationMinutes">Token expiration time in minutes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if email was sent successfully.</returns>
    Task<bool> SendPasswordResetAsync(
        string toEmail,
        string recipientName,
        string resetToken,
        int expirationMinutes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends an organization invitation email containing the invitation token.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="organizationName">Name of the organization the recipient is invited to.</param>
    /// <param name="inviterName">Display name of the user who sent the invitation.</param>
    /// <param name="invitationToken">The plaintext invitation token.</param>
    /// <param name="expiresAt">UTC timestamp when the invitation expires.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if email was sent successfully.</returns>
    Task<bool> SendInvitationAsync(
        string toEmail,
        string organizationName,
        string inviterName,
        string invitationToken,
        DateTime expiresAt,
        CancellationToken cancellationToken);

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
        string? textBody,
        CancellationToken cancellationToken);
}
