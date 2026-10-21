using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// A deterministic stand-in for the credential hasher, for handler tests that are
/// about what a handler does with a fingerprint, not about how it is computed.
/// Every value maps to one fingerprint (its explicit <see cref="Map"/> entry, else
/// <c>hash:{value}</c> or the function given), whatever the purpose; the candidate
/// set of a value is that one fingerprint. The keyring's own behaviour is pinned
/// by the keyring tests over <c>KeyringCredentialHasher</c>.
/// </summary>
internal sealed class FakeCredentialHasher : CredentialHashCandidates.Issuer, ICredentialHasher
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
    private Func<string, string> _default;

    public FakeCredentialHasher(Func<string, string>? fingerprint = null)
    {
        _default = fingerprint ?? (value => "hash:" + value);
    }

    /// <summary>Every value <see cref="Hash"/> fingerprinted, in order, with its purpose.</summary>
    public List<(CredentialPurpose Purpose, string Value)> Hashed { get; } = [];

    /// <summary>Every value <see cref="Candidates"/> was asked for, in order, with its purpose.</summary>
    public List<(CredentialPurpose Purpose, string Value)> Looked { get; } = [];

    /// <summary>Every MAC check <see cref="Verify"/> was asked for, in order, with its purpose.</summary>
    public List<(CredentialPurpose Purpose, string? Value)> Verified { get; } = [];

    /// <summary>Pins the fingerprint of one value.</summary>
    public FakeCredentialHasher Map(string value, string fingerprint)
    {
        _map[value] = fingerprint;
        return this;
    }

    /// <summary>Replaces the fingerprint every unmapped value gets.</summary>
    public FakeCredentialHasher MapAll(Func<string, string> fingerprint)
    {
        _default = fingerprint;
        return this;
    }

    /// <summary>The fingerprint of <paramref name="value"/>.</summary>
    public string Of(string value) => _map.TryGetValue(value, out var mapped) ? mapped : _default(value);

    /// <summary>The candidate set a lookup of <paramref name="value"/> passes to a repository.</summary>
    public CredentialHashCandidates CandidatesFor(string value) => CandidatesOf(Of(value));

    /// <summary>A candidate set holding exactly these fingerprints (equal by value to the hasher's own).</summary>
    public static CredentialHashCandidates CandidatesOf(params string[] fingerprints) => Issue(fingerprints);

    public string Hash(CredentialPurpose purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Hashed.Add((purpose, value));
        return Of(value);
    }

    public CredentialHashCandidates Candidates(CredentialPurpose purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Looked.Add((purpose, value));
        return CandidatesFor(value);
    }

    public bool Verify(CredentialPurpose purpose, string? value, string? mac)
    {
        Verified.Add((purpose, value));
        return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(mac) && string.Equals(Of(value), mac, StringComparison.Ordinal);
    }
}
