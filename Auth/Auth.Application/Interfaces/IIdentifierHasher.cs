namespace Auth.Application.Interfaces;

/// <summary>
/// Computes stable, keyed one-way hashes of account identifiers for the
/// zero-PII tombstone registry (HMAC-SHA256 with a dedicated permanent key).
/// The hashes must remain comparable forever — identifier reservations never
/// expire — so the underlying key is never rotated.
/// </summary>
public interface IIdentifierHasher
{
    /// <summary>
    /// Hashes an email address (case- and whitespace-insensitive).
    /// </summary>
    string HashEmail(string email);

    /// <summary>
    /// Hashes a username (case- and whitespace-insensitive).
    /// </summary>
    string HashUsername(string username);
}
