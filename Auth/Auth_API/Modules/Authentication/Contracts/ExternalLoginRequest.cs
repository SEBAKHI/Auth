namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for external provider authentication (Google, Apple, etc.).
/// </summary>
public record ExternalLoginRequest
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
    /// Gets whether to create a personal organization during registration.
    /// Defaults to false.
    /// </summary>
    public bool CreateOrganization { get; init; } = false;

    /// <summary>
    /// Gets the single-use authorization code from the sign-in (Apple),
    /// exchanged server-side for the revocable refresh token that is stored
    /// (encrypted) for deletion-time revocation.
    /// </summary>
    public string? AuthorizationCode { get; init; }

    /// <summary>
    /// Gets the client-supplied first name. Apple sends the user's name only
    /// on the first authorization and never inside the ID token; the value is
    /// used solely at first registration.
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Gets the client-supplied last name (see <see cref="GivenName"/>).
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Optional stable client identifier, used only to tell one device from
    /// another when deciding whether a sign-in is worth alerting the owner
    /// about. Never an authorization input — it is client-supplied.
    /// </summary>
    public string? DeviceId { get; init; }
}
