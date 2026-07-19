namespace Auth.Application.Interfaces;

/// <summary>
/// Encrypts/decrypts the TOTP shared secret for storage at rest.
/// </summary>
public interface ITwoFactorSecretProtector
{
    /// <summary>Encrypts a plaintext TOTP secret for persistence.</summary>
    string Protect(string plainSecret);

    /// <summary>
    /// Decrypts a stored TOTP secret. Legacy plaintext values (written before
    /// encryption was added) are returned unchanged.
    /// </summary>
    string Unprotect(string storedValue);
}
