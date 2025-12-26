using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for token refresh.
/// </summary>
public record RefreshTokenRequest
{
    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    [Required]
    public required string RefreshToken { get; init; }
}
