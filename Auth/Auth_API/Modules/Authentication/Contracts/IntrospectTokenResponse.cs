using System.Text.Json.Serialization;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Response from token introspection as per RFC 7662.
/// </summary>
public class IntrospectTokenResponse
{
    /// <summary>
    /// Indicates whether the token is currently active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// The scope of the access token.
    /// </summary>
    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    /// <summary>
    /// Client identifier for the OAuth 2.0 client.
    /// </summary>
    [JsonPropertyName("client_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    /// <summary>
    /// Human-readable identifier for the resource owner.
    /// </summary>
    [JsonPropertyName("username")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    /// <summary>
    /// Type of the token (e.g., "bearer").
    /// </summary>
    [JsonPropertyName("token_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenType { get; set; }

    /// <summary>
    /// Unix timestamp of when the token expires.
    /// </summary>
    [JsonPropertyName("exp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Exp { get; set; }

    /// <summary>
    /// Unix timestamp of when the token was issued.
    /// </summary>
    [JsonPropertyName("iat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Iat { get; set; }

    /// <summary>
    /// Unix timestamp before which the token must not be accepted.
    /// </summary>
    [JsonPropertyName("nbf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Nbf { get; set; }

    /// <summary>
    /// Subject of the token (typically user ID).
    /// </summary>
    [JsonPropertyName("sub")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sub { get; set; }

    /// <summary>
    /// Audience of the token.
    /// </summary>
    [JsonPropertyName("aud")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Aud { get; set; }

    /// <summary>
    /// Issuer of the token.
    /// </summary>
    [JsonPropertyName("iss")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Iss { get; set; }

    /// <summary>
    /// JWT ID (unique identifier for this token).
    /// </summary>
    [JsonPropertyName("jti")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Jti { get; set; }

    /// <summary>
    /// Email of the user.
    /// </summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    /// <summary>
    /// Roles assigned to the user.
    /// </summary>
    [JsonPropertyName("roles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Roles { get; set; }

    /// <summary>
    /// Permissions assigned to the user.
    /// </summary>
    [JsonPropertyName("permissions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Permissions { get; set; }
}
