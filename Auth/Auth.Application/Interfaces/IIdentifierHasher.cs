namespace Auth.Application.Interfaces;

/// <summary>
/// Computes stable, keyed one-way digests of account identifiers for the
/// destruction registry (HMAC-SHA256 with a dedicated key).
///
/// <para>
/// The key is not rotatable in place: a digest written under one key cannot be
/// compared against a digest computed under another, so every live reservation
/// depends on the key staying available. Rotation is possible only alongside
/// the tombstone's KeyVersion column, and only for rows whose reservation
/// window has already elapsed.
/// </para>
///
/// <para>
/// Because the key is retained, these digests are pseudonymous — not anonymous.
/// Anyone holding the key can test a candidate address against one, which is
/// precisely what the reservation check does on every registration. The
/// registry is therefore swept on a schedule like any other personal data.
/// </para>
/// </summary>
public interface IIdentifierHasher
{
    /// <summary>
    /// Hashes an email address (case- and whitespace-insensitive).
    /// </summary>
    string HashEmail(string email);
}
