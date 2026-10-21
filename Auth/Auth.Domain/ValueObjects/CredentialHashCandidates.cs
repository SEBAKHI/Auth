namespace Auth.Domain.ValueObjects;

/// <summary>
/// Every stored fingerprint a presented credential may have been saved under:
/// one per key the keyring still accepts for that credential's purpose, current
/// key first. A lookup matches a row on ANY of them (<c>WHERE [Col] IN @Hashes</c>),
/// so a credential issued before a key rotation keeps working through the
/// rotation's overlap window.
/// </summary>
/// <remarks>
/// There is deliberately no way to build one from a single string outside the
/// credential hasher: the constructor is internal to this assembly and the
/// infrastructure hasher. A lookup written as "hash it, then match the one
/// value" would work until the first rotation and then fail every credential
/// issued under the previous key; making that a compile error is the guard.
/// </remarks>
public sealed class CredentialHashCandidates : IEquatable<CredentialHashCandidates>
{
    private readonly string[] _values;

    internal CredentialHashCandidates(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

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

    /// <summary>The fingerprints, in the keyring's order: current, next, previous, legacy.</summary>
    public IReadOnlyList<string> Values => _values;

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
}
