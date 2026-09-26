namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for resending email verification OTP.
/// </summary>
public record ResendEmailVerificationRequest
{
    /// <summary>
    /// Gets the email address to resend verification to.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
