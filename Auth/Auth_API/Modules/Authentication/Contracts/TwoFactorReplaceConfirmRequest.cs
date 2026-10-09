namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for confirming a new authenticator app.
/// </summary>
public record TwoFactorReplaceConfirmRequest
{
    /// <summary>
    /// The 6-digit code the NEW authenticator app shows, set up from the secret
    /// <c>POST /api/v1/auth/2fa/replace</c> returned.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
