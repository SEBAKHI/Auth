using System.ComponentModel.DataAnnotations;

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
    [Required]
    [StringLength(50)]
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the ID token from the external provider.
    /// </summary>
    [Required]
    public required string IdToken { get; init; }

    /// <summary>
    /// Gets the optional nonce for token replay prevention.
    /// </summary>
    [StringLength(256)]
    public string? Nonce { get; init; }

    /// <summary>
    /// Gets the TOTP code (accounts with 2FA enabled).
    /// </summary>
    [StringLength(8)]
    public string? TwoFactorCode { get; init; }
}
