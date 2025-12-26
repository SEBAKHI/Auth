namespace Auth_Lib.Configuration;

/// <summary>
/// Configuration settings for JWT token generation and validation.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Gets or sets the issuer (iss claim) for tokens.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the audience (aud claim) for tokens.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access token lifetime in minutes.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the refresh token lifetime in days.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets the path to the RSA private key file (PEM format).
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the RSA private key as a PEM string (alternative to file path).
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// Gets or sets the key ID (kid) for the JWKS endpoint.
    /// </summary>
    public string KeyId { get; set; } = "auth-key-1";

    /// <summary>
    /// Gets or sets whether to rotate tokens on refresh.
    /// </summary>
    public bool RotateRefreshTokens { get; set; } = true;

    /// <summary>
    /// Gets or sets the clock skew allowance in seconds.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>
    /// Gets the access token lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

    /// <summary>
    /// Gets the refresh token lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenLifetimeDays);

    /// <summary>
    /// Gets the clock skew as a TimeSpan.
    /// </summary>
    public TimeSpan ClockSkew => TimeSpan.FromSeconds(ClockSkewSeconds);
}
