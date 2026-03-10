using System.ComponentModel.DataAnnotations;
using Auth.Domain.Enums;

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
