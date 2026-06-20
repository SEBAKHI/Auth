using Auth.Application.Configuration;

namespace Auth.Application.Interfaces;

/// <summary>
/// Service for managing DPAPI-encrypted secrets.
/// Provides centralized secret storage, encryption, and retrieval.
/// </summary>
public interface IDpapiSecretService
{
    // ═══════════════════════════════════════════════════════════════
    // File Operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if the secret file exists at the configured path.
    /// </summary>
    bool SecretFileExists();

    /// <summary>
    /// Gets the configured path to the secret file.
    /// </summary>
    string GetSecretFilePath();

    /// <summary>
    /// Loads and decrypts the secret configuration from the DPAPI file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decrypted secret configuration, or empty configuration if file doesn't exist.</returns>
    /// <exception cref="SecretDecryptionException">When decryption fails.</exception>
    Task<SecretConfiguration> LoadSecretsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Encrypts and saves the secret configuration to the DPAPI file.
    /// Creates the directory structure if it doesn't exist.
    /// </summary>
    /// <param name="secrets">The secrets to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSecretsAsync(SecretConfiguration secrets, CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════
    // Individual Secret Operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a specific secret value by key.
    /// </summary>
    /// <param name="key">The secret key (e.g., "SmtpPassword", "GatewayToken", "Custom:mykey").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret value or null if not found.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a specific secret value.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a specific secret (custom secrets only).
    /// </summary>
    /// <param name="key">The secret key (must be in Custom dictionary).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the secret was removed, false if it didn't exist.</returns>
    Task<bool> RemoveSecretAsync(string key, CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════
    // Key Generation Operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a new RSA-2048 key pair and stores in secret configuration.
    /// WARNING: This invalidates all existing access tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public key PEM for external use.</returns>
    Task<string> GenerateRsaKeyPairAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Generates a new 256-bit HMAC key and stores in secret configuration.
    /// WARNING: This invalidates all existing refresh tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task GenerateHmacKeyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Generates a secure random gateway token and stores it.
    /// WARNING: The API Gateway will need to be reconfigured with the new token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated gateway token.</returns>
    Task<string> GenerateGatewayTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Generates all missing cryptographic keys (RSA, HMAC, Gateway token).
    /// Only generates keys that are not already configured.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of keys generated and skipped.</returns>
    Task<KeyGenerationResult> GenerateMissingKeysAsync(CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════
    // Key Import Operations (bring-your-own-keys)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Stores a caller-supplied RSA private key (and its derived public key) for JWT signing,
    /// encrypting them into the secrets file. Use when migrating a server: re-import the same key
    /// material you hold to reproduce identical, still-valid tokens on a new machine.
    /// WARNING: replaces the current signing key - all existing access tokens become invalid.
    /// </summary>
    /// <param name="privateKeyPem">The RSA private key in PEM format (PKCS#8 or PKCS#1).</param>
    /// <param name="publicKeyPem">The matching public key in SubjectPublicKeyInfo PEM format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ImportRsaKeyPairAsync(string privateKeyPem, string publicKeyPem, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a caller-supplied HMAC key (base64) for refresh token hashing, encrypting it into
    /// the secrets file.
    /// WARNING: replaces the current key - all existing refresh tokens become invalid.
    /// </summary>
    /// <param name="hmacKeyBase64">The HMAC key, base64-encoded (at least 32 bytes / 256 bits).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ImportHmacKeyAsync(string hmacKeyBase64, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a caller-supplied gateway token for inter-service authentication, encrypting it into
    /// the secrets file.
    /// WARNING: the API Gateway must be reconfigured with the same token.
    /// </summary>
    /// <param name="token">The gateway token to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ImportGatewayTokenAsync(string token, CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════
    // Status Operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the status of all configured secrets.
    /// Does not return actual secret values.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status of each secret (configured/not configured).</returns>
    Task<SecretStatusResult> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Result of key generation operation.
/// </summary>
public class KeyGenerationResult
{
    /// <summary>
    /// Whether a new RSA key pair was generated.
    /// </summary>
    public bool RsaKeyGenerated { get; set; }

    /// <summary>
    /// Whether a new HMAC key was generated.
    /// </summary>
    public bool HmacKeyGenerated { get; set; }

    /// <summary>
    /// Whether a new gateway token was generated.
    /// </summary>
    public bool GatewayTokenGenerated { get; set; }

    /// <summary>
    /// The public key PEM (only set if RSA key was generated).
    /// </summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>
    /// List of keys that were generated.
    /// </summary>
    public List<string> GeneratedKeys { get; set; } = new();

    /// <summary>
    /// List of keys that were skipped (already exist).
    /// </summary>
    public List<string> SkippedKeys { get; set; } = new();
}

/// <summary>
/// Status of configured secrets.
/// </summary>
public class SecretStatusResult
{
    /// <summary>
    /// Whether the secret file exists.
    /// </summary>
    public bool SecretFileExists { get; set; }

    /// <summary>
    /// Path to the secret file.
    /// </summary>
    public string SecretFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Machine name where secrets were created.
    /// </summary>
    public string? MachineName { get; set; }

    /// <summary>
    /// Schema version of the secret file.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Status of each individual secret.
    /// </summary>
    public Dictionary<string, SecretStatus> Secrets { get; set; } = new();
}

/// <summary>
/// Status of an individual secret.
/// </summary>
public enum SecretStatus
{
    /// <summary>
    /// Secret is not configured.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// Secret is configured with a value.
    /// </summary>
    Configured,

    /// <summary>
    /// Secret is configured but empty.
    /// </summary>
    Empty
}

/// <summary>
/// Exception thrown when secret decryption fails.
/// </summary>
public class SecretDecryptionException : Exception
{
    public SecretDecryptionException(string message) : base(message) { }
    public SecretDecryptionException(string message, Exception innerException) : base(message, innerException) { }
}
