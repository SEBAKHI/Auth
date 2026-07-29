namespace Auth.Application.Interfaces;

/// <summary>
/// Field-level encryption under a per-user data-encryption key (DEK).
/// Ciphertexts are versioned (<c>v2:</c> prefix) and bound to a field purpose,
/// so values can never be transplanted between fields or users. Destroying the
/// user's DEK (crypto-shredding, part of account destruction) renders every
/// ciphertext under it unrecoverable — including copies in backups.
/// </summary>
public interface IPerUserCryptoService
{
    /// <summary>
    /// Encrypts a value under the user's DEK, creating the DEK on first use.
    /// </summary>
    /// <param name="userId">The owning user (the DEK row has an FK to Users, so the account row must already exist).</param>
    /// <param name="plaintext">The value to encrypt.</param>
    /// <param name="purpose">Field purpose bound into the ciphertext (see <see cref="EncryptedFieldPurpose"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <c>v2:</c>-prefixed ciphertext payload.</returns>
    Task<string> EncryptAsync(Guid userId, string plaintext, string purpose, CancellationToken cancellationToken);

    /// <summary>
    /// Decrypts a <c>v2:</c> payload. Fails closed on tampering, a wrong user,
    /// a wrong purpose, or a shredded DEK. Dual-read callers must check
    /// <see cref="IsEncrypted"/> first and pass legacy values through untouched.
    /// </summary>
    Task<string> DecryptAsync(Guid userId, string ciphertext, string purpose, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the value carries the per-user ciphertext prefix.
    /// </summary>
    bool IsEncrypted(string? value);
}

/// <summary>
/// The catalog of field purposes encrypted under per-user DEKs. One constant
/// per column keeps the AAD binding consistent between writers, readers and
/// the one-time migration.
/// </summary>
public static class EncryptedFieldPurpose
{
    public const string UserPhoneNumber = "Users.PhoneNumber";
    public const string TwoFactorSecretKey = "TwoFactorAuth.SecretKey";
    public const string ExternalProviderRefreshToken = "UserExternalLogins.ProviderRefreshToken";
}
