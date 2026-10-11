using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>A public machine key, checked and in the one form it is stored in.</summary>
/// <param name="Kid">The RFC 7638 thumbprint of the key.</param>
/// <param name="PublicKeyJwk">The JWK with <c>crv</c>, <c>kty</c>, <c>x</c> and <c>y</c> only.</param>
public sealed record MachinePublicKey(string Kid, string PublicKeyJwk);

/// <summary>A key pair the server generated: the public half to store, the private half to hand back once.</summary>
/// <param name="PublicKey">What is stored.</param>
/// <param name="PrivateKeyPem">PKCS#8 PEM. Returned in one response and never stored or logged.</param>
public sealed record GeneratedMachineKey(MachinePublicKey PublicKey, string PrivateKeyPem);

/// <summary>
/// The key material of machine keys (OI-128): ES256 (ECDSA on P-256) only.
/// </summary>
public interface IMachineKeyMaterial
{
    /// <summary>Generates a new P-256 key pair.</summary>
    GeneratedMachineKey Generate();

    /// <summary>
    /// Checks a public key pasted as a JWK and returns it in stored form, or
    /// <c>MachineKey.InvalidPublicKey</c>: only <c>kty</c> <c>EC</c> on <c>crv</c> <c>P-256</c>,
    /// with <c>x</c> and <c>y</c> of a point on the curve, and no private member.
    /// </summary>
    ErrorOr<MachinePublicKey> ImportPublicJwk(string jwk);
}
