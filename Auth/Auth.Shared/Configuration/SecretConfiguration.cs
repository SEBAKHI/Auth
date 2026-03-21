namespace Auth.Shared.Configuration;

/// <summary>
/// Strongly-typed configuration for all application secrets.
/// This class represents the decrypted secret storage structure.
/// The entire object is serialized to JSON and encrypted with DPAPI.
/// </summary>
public class SecretConfiguration
{
    /// <summary>
    /// Schema version for migration support.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Timestamp when the secret file was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last modification.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Machine name where secrets were generated (for audit purposes).
    /// </summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>
    /// RSA private key in PEM format for JWT signing (PKCS#8).
    /// </summary>
    public string? JwtPrivateKeyPem { get; set; }

    /// <summary>
    /// RSA public key in PEM format for JWT validation (SubjectPublicKeyInfo).
    /// </summary>
    public string? JwtPublicKeyPem { get; set; }

    /// <summary>
    /// HMAC-SHA256 key (base64 encoded) for refresh token hashing.
    /// Must be at least 32 bytes (256 bits).
    /// </summary>
    public string? RefreshTokenHmacKey { get; set; }

    /// <summary>
    /// SMTP password for email service.
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// Gateway token for inter-service authentication.
    /// </summary>
    public string? GatewayToken { get; set; }

    /// <summary>
    /// Connection strings that contain sensitive credentials.
    /// </summary>
    public SecretConnectionStrings ConnectionStrings { get; set; } = new();

    /// <summary>
    /// Custom key-value pairs for future extensibility.
    /// </summary>
    public Dictionary<string, string> Custom { get; set; } = new();

    /// <summary>
    /// Checks if all required cryptographic keys are configured.
    /// </summary>
    public bool HasRequiredKeys =>
        !string.IsNullOrEmpty(JwtPrivateKeyPem) &&
        !string.IsNullOrEmpty(RefreshTokenHmacKey);
}

/// <summary>
/// Connection strings that may contain passwords.
/// </summary>
public class SecretConnectionStrings
{
    /// <summary>
    /// Full connection string for AuthDb (when SQL authentication is used).
    /// </summary>
    public string? AuthDb { get; set; }
}
