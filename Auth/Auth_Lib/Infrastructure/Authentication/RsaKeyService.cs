using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Auth_Lib.Infrastructure.Authentication;

/// <summary>
/// Service for RSA key generation and DPAPI encryption operations.
/// Provides utilities for generating, encrypting, and decrypting RSA private keys.
/// </summary>
public static class RsaKeyService
{
    private const string ProtectorPurpose = "JwtSigning.RsaPrivateKey";
    private const int KeySizeBits = 2048;

    /// <summary>
    /// Generates a new RSA-2048 key pair and encrypts the private key with DPAPI.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider for encrypting the private key.</param>
    /// <returns>A tuple containing the DPAPI-encrypted private key and the public key PEM.</returns>
    public static (string EncryptedPrivateKey, string PublicKeyPem) GenerateEncryptedKeyPair(
        IDataProtectionProvider dataProtectionProvider)
    {
        using var rsa = RSA.Create(KeySizeBits);

        // Export private key as PEM
        var privateKeyPem = ExportPrivateKeyPem(rsa);

        // Encrypt with DPAPI
        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        var encryptedPrivateKey = protector.Protect(privateKeyPem);

        // Export public key as PEM
        var publicKeyPem = ExportPublicKeyPem(rsa);

        return (encryptedPrivateKey, publicKeyPem);
    }

    /// <summary>
    /// Decrypts a DPAPI-encrypted RSA private key.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider for decrypting the private key.</param>
    /// <param name="encryptedPrivateKey">The DPAPI-encrypted private key.</param>
    /// <returns>The decrypted private key in PEM format.</returns>
    /// <exception cref="ArgumentException">Thrown when the encrypted key is null or empty.</exception>
    public static string DecryptPrivateKey(
        IDataProtectionProvider dataProtectionProvider,
        string encryptedPrivateKey)
    {
        if (string.IsNullOrEmpty(encryptedPrivateKey))
        {
            throw new ArgumentException("Encrypted private key cannot be null or empty.", nameof(encryptedPrivateKey));
        }

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        return protector.Unprotect(encryptedPrivateKey);
    }

    /// <summary>
    /// Exports the RSA private key in PKCS#8 PEM format.
    /// </summary>
    /// <param name="rsa">The RSA instance containing the key pair.</param>
    /// <returns>The private key in PEM format.</returns>
    private static string ExportPrivateKeyPem(RSA rsa)
    {
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var base64 = Convert.ToBase64String(privateKey);
        var pem = new StringBuilder();
        pem.AppendLine("-----BEGIN PRIVATE KEY-----");

        for (var i = 0; i < base64.Length; i += 64)
        {
            pem.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        pem.AppendLine("-----END PRIVATE KEY-----");
        return pem.ToString();
    }

    /// <summary>
    /// Exports the RSA public key in PEM format.
    /// </summary>
    /// <param name="rsa">The RSA instance containing the key pair.</param>
    /// <returns>The public key in PEM format.</returns>
    private static string ExportPublicKeyPem(RSA rsa)
    {
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var base64 = Convert.ToBase64String(publicKey);
        var pem = new StringBuilder();
        pem.AppendLine("-----BEGIN PUBLIC KEY-----");

        for (var i = 0; i < base64.Length; i += 64)
        {
            pem.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        pem.AppendLine("-----END PUBLIC KEY-----");
        return pem.ToString();
    }
}
