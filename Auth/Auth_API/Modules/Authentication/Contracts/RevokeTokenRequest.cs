using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
/// RFC 7009 specifies these as "access_token" and "refresh_token" (snake_case).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TokenTypeHint>))]
public enum TokenTypeHint
{
    /// <summary>
    /// Access token (JWT). Serialized as "access_token" per RFC 7009.
    /// </summary>
    [JsonStringEnumMemberName("access_token")]
    AccessToken,

    /// <summary>
    /// Refresh token. Serialized as "refresh_token" per RFC 7009.
    /// </summary>
    [JsonStringEnumMemberName("refresh_token")]
    RefreshToken
}
