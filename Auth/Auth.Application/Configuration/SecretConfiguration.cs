namespace Auth.Application.Configuration;

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
    /// The <see cref="Version"/> a file is saved with once its HMAC keyring holds more than
    /// key 1: the marker that survives a save by a build without the keyring. The same value
    /// as SecretFileVersions.HmacKeyringBeyondKey1 in Auth.Shared, which Application does not
    /// reference; SecretConfigurationVersionParityTests holds the two equal.
    /// </summary>
    public const int VersionWithRingBeyondKey1 = 2;

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
    /// Google OAuth client secret, with which the server exchanges the one-time code Google
    /// sends back to the apps for the ID token. Mapped to configuration as
    /// <c>ExternalAuth:Google:ClientSecret</c>.
    /// </summary>
    public string? GoogleClientSecret { get; set; }

    /// <summary>
    /// Cloudflare Turnstile's secret key for the bot check (S13): the server's half of the widget,
    /// with which it asks Cloudflare whether a token is good. Issued by Cloudflare, so never
    /// generated here (RequiredSecretsRegistry.VendorIssued). Mapped to configuration as
    /// <c>BotDefense:TurnstileSecret</c>.
    /// </summary>
    public string? TurnstileSecretKey { get; set; }

    /// <summary>
    /// SMTP password for email service.
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// Gateway token for inter-service authentication.
    /// </summary>
    public string? GatewayToken { get; set; }

    /// <summary>
    /// Argon2id password pepper(s) (base64), keyed by key id. The pepper is a server-side secret
    /// mixed into every password hash via <c>Argon2id.KnownSecret</c>. Multiple ids are retained so
    /// that hashes produced with an older pepper still verify until they are transparently rehashed
    /// on next login (rotation). Mapped to configuration as <c>Password:Pepper:Keys:{id}</c>.
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
    /// Mirrors the model in Auth.Shared; both deserialize the same secret file, and
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
    /// What the HMAC keyring remembers of every key that left it, retired or discarded
    /// (OI-217): a fingerprint of its material, never the material, so the material is
    /// refused if it is offered again. Never trimmed. Not mapped: only a keyring change
    /// reads it.
    /// </summary>
    public List<RemovedHmacKeyEntry> HmacRemovedKeys { get; set; } = new();

    /// <summary>
    /// The highest HMAC key id in use when the file was saved (OI-217): a new key takes an id
    /// above it, so no id ever names two keys. 0 in a file no build with it has saved. Not mapped.
    /// </summary>
    public int HmacHighestKeyId { get; set; }

    /// <summary>
    /// The RSA signing keyring (S12), keyed by kid. Empty until the first keyring change: an
    /// empty ring means the single <see cref="JwtPrivateKeyPem"/> is the current key, named by
    /// <c>Jwt:KeyId</c>. When it is not empty, <see cref="JwtPrivateKeyPem"/> and
    /// <see cref="JwtPublicKeyPem"/> mirror the current key on every save. Mirrors the model in
    /// Auth.Shared, and SecretConfigurationModelParityTests holds them equal. Mapped to
    /// configuration as <c>Keyring:Rsa:Keys:{kid}:*</c>.
    /// </summary>
    public Dictionary<string, SigningKeyEntry> SigningKeys { get; set; } = new();

    /// <summary>The kid of the signing key in use (S12). <c>Keyring:Rsa:CurrentKid</c>.</summary>
    public string? SigningCurrentKid { get; set; }

    /// <summary>The kid of a staged signing key, or null when none is staged (S12). <c>Keyring:Rsa:NextKid</c>.</summary>
    public string? SigningNextKid { get; set; }

    /// <summary>
    /// What the RSA signing keyring remembers of every key that left it, retired or
    /// discarded (S12): the key's RFC 7638 thumbprint, which is public, never its material,
    /// so the key is refused if it is offered again. Never trimmed. Not mapped: only a
    /// keyring change reads it.
    /// </summary>
    public List<RemovedSigningKeyEntry> SigningRemovedKeys { get; set; } = new();

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
/// A key that left the HMAC keyring, as the secrets file remembers it (OI-217).
/// </summary>
public class RemovedHmacKeyEntry
{
    /// <summary>The id the key had.</summary>
    public int Id { get; set; }

    /// <summary>SHA-256 over a fixed prefix and the key's material, base64; never the material.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>When the key left the ring.</summary>
    public DateTime? RemovedAtUtc { get; set; }

    /// <summary>How it left: <c>Retired</c> or <c>Discarded</c>.</summary>
    public string? Reason { get; set; }

    /// <summary>The key had been marked leaked.</summary>
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
/// A key that left the RSA signing keyring, as the secrets file remembers it (S12).
/// </summary>
public class RemovedSigningKeyEntry
{
    /// <summary>
    /// The key's RFC 7638 thumbprint: its kid, except for the key migrated from the single
    /// signing key, whose kid was <c>Jwt:KeyId</c>. Public, like every kid.
    /// </summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>When the key left the ring.</summary>
    public DateTime? RemovedAtUtc { get; set; }

    /// <summary>How it left: <c>Retired</c> or <c>Discarded</c>.</summary>
    public string? Reason { get; set; }

    /// <summary>The key had been marked leaked.</summary>
    public bool Compromised { get; set; }
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
