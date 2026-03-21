using System.Text.Json.Serialization;

namespace Auth.Sdk.Models;

/// <summary>
/// Result of a token introspection request (RFC 7662).
/// </summary>
public class TokenIntrospectionResult
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("sub")]
    public string? Sub { get; set; }

    [JsonPropertyName("jti")]
    public string? Jti { get; set; }

    [JsonPropertyName("iss")]
    public string? Iss { get; set; }

    [JsonPropertyName("aud")]
    public string? Aud { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("exp")]
    public long? Exp { get; set; }

    [JsonPropertyName("iat")]
    public long? Iat { get; set; }

    [JsonPropertyName("roles")]
    public string[]? Roles { get; set; }

    [JsonPropertyName("permissions")]
    public string[]? Permissions { get; set; }
}
