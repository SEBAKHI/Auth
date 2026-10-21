using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Interfaces;

/// <summary>
/// Computes the keyed fingerprints opaque credentials are stored and checked by
/// (refresh tokens, SSO sessions, codes, links, tickets, webhook keys), under the
/// HMAC keyring, with each <see cref="CredentialPurpose"/> kept apart from every
/// other.
/// </summary>
/// <remarks>
/// Three operations, one per way a fingerprint is used:
/// <list type="bullet">
/// <item><see cref="Hash"/> — to STORE a new credential's fingerprint, or to sign
/// one (always under the issuing key).</item>
/// <item><see cref="Candidates"/> — to FIND a stored row by a presented credential:
/// every fingerprint it may have been stored under, for a <c>WHERE … IN</c> lookup.
/// A single <see cref="Hash"/> result can never be passed where a lookup expects
/// candidates.</item>
/// <item><see cref="Verify"/> — to CHECK a presented credential against a MAC the
/// caller already holds (a cookie, a ticket signature, a stored code), in fixed
/// time against every accepted key.</item>
/// </list>
/// </remarks>
public interface ICredentialHasher
{
    /// <summary>The fingerprint of <paramref name="value"/> under the issuing key.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    string Hash(CredentialPurpose purpose, string value);

    /// <summary>Every fingerprint a stored <paramref name="value"/> may carry, issuing key first.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    CredentialHashCandidates Candidates(CredentialPurpose purpose, string value);

    /// <summary>
    /// Whether <paramref name="mac"/> is the fingerprint of <paramref name="value"/>
    /// under any key the purpose still accepts. False for an empty value or MAC.
    /// </summary>
    bool Verify(CredentialPurpose purpose, string? value, string? mac);
}
