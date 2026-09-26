namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for recovering an account pending deletion during its grace
/// window, authenticated by an external identity provider's ID token.
/// </summary>
public record RecoverAccountExternalRequest
{
    /// <summary>
    /// Gets the external provider code (e.g., "google").
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ID token from the external provider.
    /// </summary>
    public string IdToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional nonce for token replay prevention.
    /// </summary>
    public string? Nonce { get; init; }

    /// <summary>
    /// Gets the TOTP code (accounts with 2FA enabled).
    /// </summary>
    public string? TwoFactorCode { get; init; }
}
