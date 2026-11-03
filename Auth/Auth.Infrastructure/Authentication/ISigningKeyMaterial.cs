using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// The keys this server signs access tokens with and accepts them under (S12): the one source of
/// both. The signer reads <see cref="Current"/> each time it signs, every validator resolves its
/// keys from <see cref="VerificationKeys"/>, and the JWKS and the PEM are written from
/// <see cref="PublishedKeys"/>.
/// </summary>
/// <remarks>
/// A second place that holds a key is the copy a rotation misses: the validator that still trusts
/// the old key, or the signer that still uses it. So nothing else captures a key; each reads it
/// here when it needs it. The implementation is the signing keyring (<see cref="SigningKeyRingHolder"/>),
/// whose snapshot a keyring change swaps whole, so the next token signed or checked uses the new keys.
/// </remarks>
public interface ISigningKeyMaterial
{
    /// <summary>The credentials every access token is signed with now: RS256 over the current key.</summary>
    SigningCredentials Current { get; }

    /// <summary>The keys a token this server signed may carry a signature of, at <paramref name="now"/>.</summary>
    IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now);

    /// <summary>
    /// The public keys the JWKS lists at <paramref name="now"/>, the current key first. The PEM is
    /// the first of them.
    /// </summary>
    IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now);
}
