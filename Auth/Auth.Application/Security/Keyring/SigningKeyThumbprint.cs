using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The name of every new signing key (S12, KR2): its RFC 7638 JWK thumbprint, base64url.
/// The same function names a generated key, a staged import, and the key a new install
/// writes, so a key has one name wherever it comes from.
/// </summary>
/// <remarks>
/// The thumbprint is SHA-256 over the key's public JWK members (<c>e</c>, <c>kty</c>,
/// <c>n</c>) in their canonical order, so it is public, fixed for the key, and distinct
/// for another key. RFC 7638 §3.1 gives the test vector this is checked against.
/// </remarks>
public static class SigningKeyThumbprint
{
    /// <summary>The thumbprint of <paramref name="rsa"/>'s public key.</summary>
    public static string Of(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        return Base64UrlEncoder.Encode(new RsaSecurityKey(rsa).ComputeJwkThumbprint());
    }

    /// <summary>The thumbprint of the key in <paramref name="pem"/>, private or public.</summary>
    /// <exception cref="ArgumentException">The PEM holds no RSA key.</exception>
    /// <exception cref="CryptographicException">The PEM's key does not decode.</exception>
    public static string OfPem(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return Of(rsa);
    }
}
