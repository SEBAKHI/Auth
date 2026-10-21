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
    /// HMAC-SHA256 key (base64, >= 32 bytes) for account-deletion identifier
    /// hashing (tombstone reservations). PERMANENT: never rotate — identifier
    /// reservations and restore re-application depend on stable hashes.
    /// Mapped to configuration as <c>AccountDeletion:IdentifierHmacKeyPlain</c>.
    /// </summary>
    public string? AccountDeletionIdentifierHmacKey { get; set; }

    /// <summary>
    /// Apple "Sign in with Apple" .p8 signing key (PKCS#8 PEM) used to mint
    /// ES256 client secrets. Mapped to configuration as
    /// <c>ExternalAuth:Apple:PrivateKeyPem</c>.
    /// </summary>
    public string? AppleSigningKeyPem { get; set; }

    /// <summary>
    /// SMTP password for email service.
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// Gateway token for inter-service authentication.
    /// </summary>
    public string? GatewayToken { get; set; }

    /// <summary>
    /// Argon2id password pepper(s) (base64), keyed by key id. Mirrors the model in
    /// Auth.Application; both deserialize the same secret file. Mapped to configuration as
    /// <c>Password:Pepper:Keys:{id}</c>.
    /// </summary>
    public Dictionary<int, string> PasswordPeppers { get; set; } = new();

    /// <summary>
    /// The id of the pepper used to hash NEW passwords. 0 means none. Mapped to configuration as
    /// <c>Password:Pepper:CurrentKeyId</c>.
    /// </summary>
    public int PasswordPepperCurrentKeyId { get; set; }

    /// <summary>
    /// The HMAC keyring behind every opaque credential's fingerprint (S11), keyed by key id.
    /// Empty until the file is written by a build that knows it: an empty ring means the
    /// single <see cref="RefreshTokenHmacKey"/> is key 1. When it is not empty,
    /// <see cref="RefreshTokenHmacKey"/> mirrors the current key's material on every save.
    /// Mirrors the model in Auth.Application; both deserialize the same secret file, and
    /// SecretConfigurationModelParityTests holds them equal. Mapped to configuration as
    /// <c>Keyring:Hmac:Keys:{id}:*</c>.
    /// </summary>
    public Dictionary<int, HmacKeyEntry> HmacKeys { get; set; } = new();

    /// <summary>The id of the key that issues. 0 while the ring is empty. <c>Keyring:Hmac:CurrentKeyId</c>.</summary>
    public int HmacCurrentKeyId { get; set; }

    /// <summary>The id of a staged key, or 0 when none is staged. <c>Keyring:Hmac:NextKeyId</c>.</summary>
    public int HmacNextKeyId { get; set; }

    /// <summary>When issuing moved from the legacy form to purpose subkeys. <c>Keyring:Hmac:LegacyCutoverUtc</c>.</summary>
    public DateTime? HmacLegacyCutoverUtc { get; set; }

    /// <summary>The latest instant the legacy form is accepted. <c>Keyring:Hmac:LegacyVerifyUntilUtc</c>.</summary>
    public DateTime? HmacLegacyVerifyUntilUtc { get; set; }

    /// <summary>
    /// The RSA signing keyring, keyed by kid. Empty, and not mapped, until S12 uses it. Present
    /// now so a build that predates S12 does not erase it from the file on its first save.
    /// </summary>
    public Dictionary<string, SigningKeyEntry> SigningKeys { get; set; } = new();

    /// <summary>The kid of the signing key in use (S12).</summary>
    public string? SigningCurrentKid { get; set; }

    /// <summary>The kid of a staged signing key (S12).</summary>
    public string? SigningNextKid { get; set; }

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
/// One key of the HMAC keyring as the secrets file stores it.
/// </summary>
public class HmacKeyEntry
{
    /// <summary>The key material, base64, at least 32 bytes.</summary>
    public string Material { get; set; } = string.Empty;

    /// <summary>When the key was created or staged.</summary>
    public DateTime? CreatedAtUtc { get; set; }

    /// <summary>The latest instant a verify-only key is accepted for a bounded purpose.</summary>
    public DateTime? VerifyUntilUtc { get; set; }

    /// <summary>When the key stopped issuing.</summary>
    public DateTime? DemotedAtUtc { get; set; }

    /// <summary>The key leaked; it verifies nothing but webhook keys.</summary>
    public bool Compromised { get; set; }
}

/// <summary>
/// One key of the RSA signing keyring as the secrets file stores it (S12).
/// </summary>
public class SigningKeyEntry
{
    /// <summary>The private key in PEM, held only for the next and current keys.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>The public key in PEM.</summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>When the key was created.</summary>
    public DateTime? CreatedAtUtc { get; set; }

    /// <summary>When the key was first published in the JWKS.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>The latest instant tokens signed by this key are accepted.</summary>
    public DateTime? VerifyUntilUtc { get; set; }

    /// <summary>The key leaked; it is neither published nor accepted.</summary>
    public bool Compromised { get; set; }
}

/// <summary>
/// The values <see cref="SecretConfiguration.Version"/> takes.
/// </summary>
/// <remarks>
/// A build without the HMAC keyring keeps an unknown <c>Version</c> through a save
/// (it reads and writes the member) but erases the keyring (it has no member for
/// it). So the version is what survives to say a keyring existed: a file at
/// <see cref="HmacKeyringBeyondKey1"/> with no keyring was saved by such a build
/// after a rotation, and starting on its single key would issue under the wrong key.
/// <para>
/// <c>Version &gt;= 2</c> means exactly that and nothing else. A later change to the
/// file (the RSA ring of S12 included) takes a marker of its own and never raises
/// <c>Version</c> for another reason, or this check would refuse files it should not.
/// </para>
/// </remarks>
public static class SecretFileVersions
{
    /// <summary>Every file before the keyring, and a keyring that holds key 1 alone.</summary>
    public const int Initial = 1;

    /// <summary>The keyring holds more than key 1, or has moved off the legacy form.</summary>
    public const int HmacKeyringBeyondKey1 = 2;
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
