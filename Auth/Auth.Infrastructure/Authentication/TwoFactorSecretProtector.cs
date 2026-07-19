using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Encrypts the TOTP shared secret at rest using the application's Data
/// Protection key ring (the same one that protects the JWT signing key). The
/// secret must be reversible (it is needed to compute TOTP codes), so it is
/// encrypted — not hashed like passwords or recovery codes.
/// </summary>
public class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    // Versioned purpose string: rotating it (v2, ...) would invalidate old
    // ciphertexts, so keep it stable.
    private const string Purpose = "TwoFactorAuth.SecretKey.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<TwoFactorSecretProtector> _logger;

    public TwoFactorSecretProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TwoFactorSecretProtector> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Protect(string plainSecret) => _protector.Protect(plainSecret);

    /// <inheritdoc />
    public string Unprotect(string storedValue)
    {
        try
        {
            return _protector.Unprotect(storedValue);
        }
        catch (CryptographicException)
        {
            // Legacy row written before encryption existed (plaintext Base32).
            // Return it as-is so the existing 2FA user is not locked out; it is
            // re-encrypted the next time the row is written (enable/disable/reset).
            _logger.LogWarning(
                "TOTP secret is not Data-Protected (legacy plaintext); using it as-is. It will be encrypted on the next write.");
            return storedValue;
        }
    }
}
