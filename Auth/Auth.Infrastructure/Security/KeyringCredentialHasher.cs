using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Infrastructure.Security;

/// <summary>
/// <see cref="ICredentialHasher"/> over the HMAC keyring (S11).
/// </summary>
/// <remarks>
/// The legacy form is HMAC-SHA256 under key 1's raw material over the UTF-8
/// value, base64: byte for byte what every deployment stored before the keyring,
/// so a ring that has never rotated issues and finds exactly today's values.
/// <para>
/// A purpose subkey is SP 800-108r1 KBKDF in counter mode with HMAC-SHA256
/// (<see cref="SP800108HmacCounterKdf"/>): label = the purpose's fixed label, empty
/// context, 256 bits. It is derived once per key and purpose and cached against
/// that key instance, so the cache can never outlive the material it came from.
/// </para>
/// </remarks>
public sealed class KeyringCredentialHasher : CredentialHashCandidates.Issuer, ICredentialHasher
{
    private const int SubkeyBytes = 32;

    private readonly HmacKeyringHolder _holder;
    private readonly TimeProvider _timeProvider;
    private readonly ConditionalWeakTable<HmacKey, ConcurrentDictionary<CredentialPurpose, byte[]>> _subkeys = new();

    public KeyringCredentialHasher(HmacKeyringHolder holder, TimeProvider timeProvider)
    {
        _holder = holder;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Hash(CredentialPurpose purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        var ring = _holder.Current;
        var rule = CredentialPurposePolicy.For(purpose);
        return Fingerprint(ring.Issuing, purpose, rule, Encoding.UTF8.GetBytes(value));
    }

    /// <inheritdoc />
    public CredentialHashCandidates Candidates(CredentialPurpose purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return Issue(purpose, AllFingerprints(purpose, value));
    }

    /// <inheritdoc />
    public bool Verify(CredentialPurpose purpose, string? value, string? mac)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(mac))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(mac);
        var matched = false;

        // Every accepted key is compared, in fixed time, without stopping at the
        // first match: how long this takes says nothing about which key matched.
        foreach (var candidate in AllFingerprints(purpose, value))
        {
            matched |= CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), presented);
        }

        return matched;
    }

    private List<string> AllFingerprints(CredentialPurpose purpose, string value)
    {
        // One ring for the whole operation (see HmacKeyringHolder).
        var ring = _holder.Current;
        var rule = CredentialPurposePolicy.For(purpose);
        var message = Encoding.UTF8.GetBytes(value);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return ring.Accepted(purpose, now)
            .Select(derivation => Fingerprint(derivation, purpose, rule, message))
            .ToList();
    }

    private string Fingerprint(HmacDerivation derivation, CredentialPurpose purpose, CredentialPurposeRule rule, byte[] message)
    {
        var mac = derivation.IsLegacy
            ? HMACSHA256.HashData(derivation.Key.Material, message)
            : HMACSHA256.HashData(Subkey(derivation.Key, purpose, rule), message);

        return rule.Format.Write(derivation, Convert.ToBase64String(mac));
    }

    private byte[] Subkey(HmacKey key, CredentialPurpose purpose, CredentialPurposeRule rule) =>
        _subkeys.GetValue(key, static _ => new ConcurrentDictionary<CredentialPurpose, byte[]>())
            .GetOrAdd(purpose, static (_, state) => SP800108HmacCounterKdf.DeriveBytes(
                    state.Key.Material,
                    HashAlgorithmName.SHA256,
                    Encoding.UTF8.GetBytes(state.Label),
                    ReadOnlySpan<byte>.Empty,
                    SubkeyBytes),
                (Key: key, Label: rule.DerivationLabel));
}
