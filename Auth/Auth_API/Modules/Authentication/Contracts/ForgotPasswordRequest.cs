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

    /// <summary>
    /// The one-time token the page's bot-check widget produced (S13), at most 2048 characters.
    /// Optional: omitted while the check is off, and an older server ignores it.
    /// </summary>
    public string? BotToken { get; init; }
}
