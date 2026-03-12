using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for external provider authentication (Google, Apple, etc.).
/// </summary>
public record ExternalLoginRequest
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
    /// Gets whether to create a personal organization during registration.
    /// Defaults to false.
    /// </summary>
    public bool CreateOrganization { get; init; } = false;
}
