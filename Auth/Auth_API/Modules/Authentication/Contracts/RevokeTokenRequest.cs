using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request to revoke an access or refresh token.
/// </summary>
public class RevokeTokenRequest
{
    /// <summary>
    /// The token to revoke (access token or refresh token).
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The type of token being revoked.
    /// </summary>
    public TokenTypeHint? TokenTypeHint { get; set; }
}

/// <summary>
/// Hint about the type of token being revoked.
/// </summary>
public enum TokenTypeHint
{
    /// <summary>
    /// Access token (JWT).
    /// </summary>
    AccessToken,

    /// <summary>
    /// Refresh token.
    /// </summary>
    RefreshToken
}
