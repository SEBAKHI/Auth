using System.Text.Json.Serialization;

namespace Auth.Sdk.Models;

/// <summary>
/// Response from the token refresh endpoint.
/// </summary>
public record TokenRefreshResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; init; }
}
