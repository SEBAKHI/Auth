namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for initiating a password reset.
/// </summary>
public record ForgotPasswordRequest
{
    /// <summary>
    /// Gets the email address of the account to reset.
    /// </summary>
    public string Email { get; init; } = string.Empty;
}
