namespace Auth.Application.Interfaces;

/// <summary>
/// Encrypts/decrypts the TOTP shared secret for storage at rest under the
/// user's per-user encryption key.
/// </summary>
public interface ITwoFactorSecretProtector
{
    /// <summary>
    /// Encrypts a plaintext TOTP secret for persistence under the user's DEK.
    /// </summary>
    Task<string> ProtectAsync(Guid userId, string plainSecret, CancellationToken cancellationToken);

    /// <summary>
    /// Decrypts a stored TOTP secret. Dual-read: per-user (<c>v2:</c>)
    /// payloads, legacy app-level Data Protection payloads, and pre-encryption
    /// plaintext values (returned unchanged) are all handled.
    /// </summary>
    Task<string> UnprotectAsync(Guid userId, string storedValue, CancellationToken cancellationToken);
}
