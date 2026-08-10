using System.Security.Cryptography;
using System.Text;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Digests caller-supplied key material so a confirmation can be bound to the
/// exact bytes that were approved.
/// </summary>
/// <remarks>
/// A plain SHA-256, deliberately: this is not a credential check. The input is
/// high-entropy key material the caller already holds in full, so there is
/// nothing for a salted, slow hash to protect — the digest exists only to make
/// "the material I confirmed" and "the material I submitted" comparable. It is
/// never stored in place of the key and never verified against a secret.
/// </remarks>
public static class SecretPayloadDigest
{
    /// <summary>
    /// Computes the lowercase hex SHA-256 digest of key material, or null when
    /// the operation carries no payload (the generate operations).
    /// </summary>
    public static string? Compute(string? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
