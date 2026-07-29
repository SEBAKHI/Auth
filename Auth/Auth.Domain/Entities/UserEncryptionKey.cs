namespace Auth.Domain.Entities;

/// <summary>
/// Per-user data-encryption key (DEK), created lazily on the first encrypted
/// write. The wrapped key is opaque to the domain: infrastructure wraps the
/// 32-byte AES-256-GCM key with Data Protection. Deleting the row is the
/// crypto-shredding step of account destruction — every ciphertext under this
/// key becomes unrecoverable in the database and in all backups.
/// Keyed by UserId (no separate identity).
/// </summary>
public class UserEncryptionKey
{
    /// <summary>
    /// The only algorithm approved for per-user field encryption.
    /// </summary>
    public const string DefaultAlgorithm = "AES-256-GCM";

    /// <summary>
    /// Gets the ID of the user this key belongs to (primary key).
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the Data-Protection-wrapped DEK (base64 payload).
    /// </summary>
    public string WrappedDek { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the key version (supports future master-key rotation).
    /// </summary>
    public int KeyVersion { get; private set; }

    /// <summary>
    /// Gets the symmetric algorithm the DEK is used with.
    /// </summary>
    public string Algorithm { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when this key was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private UserEncryptionKey()
    {
    }

    public UserEncryptionKey(
        Guid userId,
        string wrappedDek,
        int keyVersion,
        string algorithm,
        DateTime createdAt)
    {
        UserId = userId;
        WrappedDek = wrappedDek;
        KeyVersion = keyVersion;
        Algorithm = algorithm;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a version-1 key for a user.
    /// </summary>
    /// <param name="userId">The owning user.</param>
    /// <param name="wrappedDek">The Data-Protection-wrapped DEK.</param>
    public static UserEncryptionKey Create(Guid userId, string wrappedDek)
    {
        return new UserEncryptionKey
        {
            UserId = userId,
            WrappedDek = wrappedDek,
            KeyVersion = 1,
            Algorithm = DefaultAlgorithm,
            CreatedAt = DateTime.UtcNow
        };
    }
}
