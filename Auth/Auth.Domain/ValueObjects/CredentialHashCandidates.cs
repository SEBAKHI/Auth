using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// Every stored fingerprint a presented credential may have been saved under:
/// one per key the keyring still accepts for that credential's purpose, current
/// key first. A lookup matches a row on ANY of them (<c>WHERE [Col] IN @Hashes</c>),
/// so a credential issued before a key rotation keeps working through the
/// rotation's overlap window.
/// </summary>
/// <remarks>
/// There is deliberately no way to build one from a string: the constructor is
/// private, and only an <see cref="Issuer"/> — the credential hasher — can call it.
/// A lookup written as "hash it, then match the one value" would work until the
/// first rotation and then fail every credential issued under the previous key;
/// making that a compile error is the guard.
/// <para>
/// A set also carries the <see cref="CredentialPurpose"/> it was computed for, and a
/// repository reads its fingerprints only through <see cref="ValuesFor"/>, naming the
/// purpose its column holds. Until the first rotation every purpose computes the same
/// legacy fingerprint of the same value, so a refresh-token lookup handed an SSO
/// session's candidates would work by accident and break at the rotation; the purpose
/// check makes that mistake fail the first time it runs (S11 SEC-5).
/// </para>
/// <para>
/// Equality is over the fingerprints alone: two sets that hold the same fingerprints
/// find the same rows. The purpose is a check the repository makes, not part of what
/// the set matches.
/// </para>
/// </remarks>
public sealed class CredentialHashCandidates : IEquatable<CredentialHashCandidates>
{
    private readonly string[] _values;

    private CredentialHashCandidates(CredentialPurpose purpose, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Purpose = purpose;
        _values = values.Distinct(StringComparer.Ordinal).ToArray();

        if (_values.Length == 0)
        {
            throw new ArgumentException("A candidate set holds at least one fingerprint.", nameof(values));
        }

        if (_values.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("A fingerprint is never empty.", nameof(values));
        }
    }

    /// <summary>The purpose the fingerprints were computed for.</summary>
    public CredentialPurpose Purpose { get; }

    /// <summary>The fingerprints, in the keyring's order: current, next, previous, legacy.</summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>
    /// The fingerprints, for a lookup in a column that holds <paramref name="expected"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The set was computed for another purpose: a programming error, never a user's.
    /// </exception>
    public IReadOnlyList<string> ValuesFor(CredentialPurpose expected) =>
        Purpose == expected
            ? _values
            : throw new InvalidOperationException(
                $"Candidates computed for {Purpose} were passed to a lookup of {expected}.");

    /// <summary>Whether a stored fingerprint is one of the candidates (ordinal).</summary>
    public bool Contains(string? storedValue) =>
        storedValue is not null && Array.IndexOf(_values, storedValue) >= 0;

    public bool Equals(CredentialHashCandidates? other) =>
        other is not null && _values.AsSpan().SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as CredentialHashCandidates);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _values)
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    // The fingerprints name credentials; keep them out of log lines.
    public override string ToString() => $"CredentialHashCandidates {{ Count = {_values.Length} }}";

    /// <summary>
    /// The one kind of type allowed to build a candidate set: the credential hasher,
    /// which computes every fingerprint itself. A guard test pins which classes
    /// derive from it, so a lookup cannot quietly build one from a stored string.
    /// </summary>
    public abstract class Issuer
    {
        /// <summary>The candidate set of these fingerprints of <paramref name="purpose"/>, in the keyring's order.</summary>
        protected static CredentialHashCandidates Issue(CredentialPurpose purpose, IEnumerable<string> fingerprints) =>
            new(purpose, fingerprints);
    }
}
