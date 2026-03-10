using System.Text.Json.Serialization;

namespace Auth.Domain.Enums;

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
