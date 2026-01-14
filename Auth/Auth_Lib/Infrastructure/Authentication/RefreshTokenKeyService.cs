using System.Security.Cryptography;
using System.Text;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Auth_Lib.Infrastructure.Authentication;

/// <summary>
/// Service for managing HMAC key operations for refresh tokens.
/// The HMAC key is protected at rest using Windows DPAPI (Data Protection API).
/// </summary>
public class RefreshTokenKeyService : IRefreshTokenKeyService
{
    private readonly byte[] _hmacKey;
    private const string ProtectorPurpose = "RefreshTokens.HmacKey";

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenKeyService"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider for decrypting the HMAC key.</param>
    /// <param name="settings">The JWT settings containing the encrypted HMAC key.</param>
    /// <exception cref="InvalidOperationException">Thrown when the encrypted key is not configured.</exception>
    public RefreshTokenKeyService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<JwtSettings> settings)
    {
        var encryptedKey = settings.Value.RefreshTokenEncryptedKey;

        if (string.IsNullOrEmpty(encryptedKey))
        {
            throw new InvalidOperationException(
                "RefreshTokenEncryptedKey is not configured. " +
                "Generate a key using KeyGeneratorTool and add it to appsettings.json under JwtSettings.RefreshTokenEncryptedKey");
        }

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

        // Decrypt the HMAC key at startup
        var decryptedKeyBase64 = protector.Unprotect(encryptedKey);
        _hmacKey = Convert.FromBase64String(decryptedKeyBase64);

        // Validate key size (should be at least 32 bytes / 256 bits for HMAC-SHA256)
        if (_hmacKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"HMAC key must be at least 32 bytes (256 bits). Current key is {_hmacKey.Length} bytes.");
        }
    }

    /// <inheritdoc />
    public string ComputeTokenHash(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));
        }

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        using var hmac = new HMACSHA256(_hmacKey);
        var hashBytes = hmac.ComputeHash(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Generates a new HMAC key and encrypts it using DPAPI.
    /// This is a static utility method for initial setup.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <returns>The DPAPI-encrypted HMAC key as a base64 string.</returns>
    public static string GenerateEncryptedKey(IDataProtectionProvider dataProtectionProvider)
    {
        // Generate 32-byte (256-bit) random key
        var key = RandomNumberGenerator.GetBytes(32);
        var keyBase64 = Convert.ToBase64String(key);

        // Encrypt with DPAPI
        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        var encryptedKey = protector.Protect(keyBase64);

        return encryptedKey;
    }
}
