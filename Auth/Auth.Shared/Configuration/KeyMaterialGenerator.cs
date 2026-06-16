using System.Security.Cryptography;
using System.Text;

namespace Auth.Shared.Configuration;

/// <summary>
/// Central, single source of truth for generating the application's cryptographic
/// key material (RSA signing key pair, HMAC key, gateway token) and formatting keys as PEM.
/// Used by every storage mode so the generated material is identical regardless of
/// where it is ultimately stored (plain text, certificate-protected file, or DPAPI file).
/// </summary>
public static class KeyMaterialGenerator
{
    private const int RsaKeySizeBits = 2048;
    private const int HmacKeySizeBytes = 32;     // 256-bit HMAC-SHA256 key
    private const int GatewayTokenSizeBytes = 32; // 256-bit gateway token

    /// <summary>
    /// Generates a new RSA-2048 key pair and returns both keys in PEM format.
    /// </summary>
    /// <returns>The PKCS#8 private key PEM and the SubjectPublicKeyInfo public key PEM.</returns>
    public static (string PrivateKeyPem, string PublicKeyPem) GenerateRsaKeyPair()
    {
        using var rsa = RSA.Create(RsaKeySizeBits);
        return (ToPrivateKeyPem(rsa), ToPublicKeyPem(rsa));
    }

    /// <summary>
    /// Generates a new 256-bit HMAC key encoded as base64.
    /// </summary>
    public static string GenerateHmacKeyBase64()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(HmacKeySizeBytes));
    }

    /// <summary>
    /// Generates a new 256-bit gateway token encoded as base64.
    /// </summary>
    public static string GenerateGatewayToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(GatewayTokenSizeBytes));
    }

    /// <summary>
    /// Exports an RSA private key in PKCS#8 PEM format.
    /// </summary>
    public static string ToPrivateKeyPem(RSA rsa)
    {
        return FormatPem("PRIVATE KEY", rsa.ExportPkcs8PrivateKey());
    }

    /// <summary>
    /// Exports an RSA public key in SubjectPublicKeyInfo PEM format.
    /// </summary>
    public static string ToPublicKeyPem(RSA rsa)
    {
        return FormatPem("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());
    }

    private static string FormatPem(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der);
        var pem = new StringBuilder();
        pem.AppendLine($"-----BEGIN {label}-----");

        for (var i = 0; i < base64.Length; i += 64)
        {
            pem.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        pem.AppendLine($"-----END {label}-----");
        return pem.ToString();
    }
}
