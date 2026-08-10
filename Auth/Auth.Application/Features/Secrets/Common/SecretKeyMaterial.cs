using System.Security.Cryptography;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Shape and strength checks for caller-supplied key material, shared by the
/// confirmation flow and the import handlers.
/// </summary>
/// <remarks>
/// Both callers need the identical verdict, and they need it at different
/// times: the confirmation flow runs these checks before a code is emailed, so
/// a typo in a PEM costs a validation error rather than a round trip through
/// the administrator's mailbox; the import handler runs them again at execution
/// because the material submitted then is what actually gets stored.
/// </remarks>
public static class SecretKeyMaterial
{
    /// <summary>Smallest RSA modulus accepted for a signing key.</summary>
    public const int MinimumRsaKeySizeBits = 2048;

    /// <summary>Smallest accepted HMAC key, in bytes (256 bits).</summary>
    public const int MinimumHmacKeyBytes = 32;

    /// <summary>Shortest accepted gateway token.</summary>
    public const int MinimumGatewayTokenLength = 16;

    /// <summary>
    /// Validates an RSA private key in PEM form and derives its public key.
    /// </summary>
    /// <param name="privateKeyPem">The private key in PKCS#8 or PKCS#1 PEM form.</param>
    /// <returns>The derived SubjectPublicKeyInfo PEM, or a validation error.</returns>
    public static ErrorOr<string> ValidateRsaPrivateKey(string privateKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            if (rsa.KeySize < MinimumRsaKeySizeBits)
            {
                return SecretErrors.InvalidKeyMaterial(
                    $"RSA key size {rsa.KeySize} is below the required {MinimumRsaKeySizeBits}-bit minimum.");
            }

            // Confirm the PEM actually carries a private key (not just a public key) before storing it.
            _ = rsa.ExportPkcs8PrivateKey();
            return rsa.ExportSubjectPublicKeyInfoPem();
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return SecretErrors.InvalidKeyMaterial(
                "The value is not a valid RSA private key in PEM format (expected PKCS#8 or PKCS#1).");
        }
    }

    /// <summary>
    /// Validates that an HMAC key is base64 and long enough.
    /// </summary>
    public static ErrorOr<Success> ValidateHmacKey(string hmacKeyBase64)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(hmacKeyBase64);
        }
        catch (FormatException)
        {
            return SecretErrors.InvalidKeyMaterial("The HMAC key must be a valid Base64 string.");
        }

        if (decoded.Length < MinimumHmacKeyBytes)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC key must decode to at least {MinimumHmacKeyBytes} bytes (256 bits).");
        }

        return Result.Success;
    }

    /// <summary>
    /// Validates a gateway token. It is compared as an opaque string at runtime,
    /// so only a minimum length is enforced, to reject trivially weak values.
    /// </summary>
    public static ErrorOr<Success> ValidateGatewayToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < MinimumGatewayTokenLength)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The gateway token must be at least {MinimumGatewayTokenLength} characters.");
        }

        return Result.Success;
    }
}
