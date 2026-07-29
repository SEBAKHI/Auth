using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Encrypts the TOTP shared secret at rest under the user's per-user DEK
/// (crypto-shredded with the account). The secret must be reversible (it is
/// needed to compute TOTP codes), so it is encrypted — not hashed like
/// passwords or recovery codes. Dual-read keeps every generation of stored
/// value working: per-user <c>v2:</c> payloads, legacy app-level Data
/// Protection payloads (v1), and pre-encryption plaintext rows; the one-time
/// encryption migration (and any subsequent write) upgrades old rows to v2.
/// </summary>
public class TwoFactorSecretProtector : ITwoFactorSecretProtector
{
    // The legacy purpose string of app-level (v1) payloads. Rotating it would
    // make existing v1 ciphertexts undecryptable, so keep it stable until the
    // migration has upgraded every row.
    private const string LegacyPurpose = "TwoFactorAuth.SecretKey.v1";

    private readonly IPerUserCryptoService _perUserCrypto;
    private readonly IDataProtector _legacyProtector;
    private readonly ILogger<TwoFactorSecretProtector> _logger;

    public TwoFactorSecretProtector(
        IPerUserCryptoService perUserCrypto,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TwoFactorSecretProtector> logger)
    {
        _perUserCrypto = perUserCrypto;
        _legacyProtector = dataProtectionProvider.CreateProtector(LegacyPurpose);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> ProtectAsync(Guid userId, string plainSecret, CancellationToken cancellationToken) =>
        _perUserCrypto.EncryptAsync(userId, plainSecret, EncryptedFieldPurpose.TwoFactorSecretKey, cancellationToken);

    /// <inheritdoc />
    public async Task<string> UnprotectAsync(Guid userId, string storedValue, CancellationToken cancellationToken)
    {
        if (_perUserCrypto.IsEncrypted(storedValue))
        {
            return await _perUserCrypto.DecryptAsync(
                userId, storedValue, EncryptedFieldPurpose.TwoFactorSecretKey, cancellationToken);
        }

        try
        {
            // Legacy app-level payload (v1); upgraded to v2 by the one-time
            // migration or the next write (enable/disable/reset).
            return _legacyProtector.Unprotect(storedValue);
        }
        catch (CryptographicException)
        {
            // Legacy row written before encryption existed (plaintext Base32).
            // Return it as-is so the existing 2FA user is not locked out.
            _logger.LogWarning(
                "TOTP secret is not encrypted (legacy plaintext); using it as-is. It will be encrypted on the next write.");
            return storedValue;
        }
    }
}
