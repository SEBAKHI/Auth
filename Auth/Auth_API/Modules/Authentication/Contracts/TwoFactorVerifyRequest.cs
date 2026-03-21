namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for two-factor verification.
/// </summary>
public record TwoFactorVerifyRequest
{
    /// <summary>
    /// The 6-digit TOTP code from the authenticator app.
    /// </summary>
    public required string Code { get; init; }
}
