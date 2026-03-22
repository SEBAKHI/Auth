using System.Text.Json.Serialization;

namespace Auth.Sdk.Models;

/// <summary>
/// Response from the login endpoint. Maps to Auth.Application.DTOs.LoginResponse.
/// </summary>
internal record LoginResult
{
    [JsonPropertyName("token")]
    public TokenInfo? Token { get; init; }
}

/// <summary>
/// Token information nested within the login response.
/// </summary>
internal record TokenInfo
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; init; }
}
