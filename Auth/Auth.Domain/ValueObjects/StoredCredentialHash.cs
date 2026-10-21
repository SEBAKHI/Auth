namespace Auth.Domain.ValueObjects;

/// <summary>
/// A fingerprint exactly as one row stored it, used to find the row that another
/// row points at (a rotated refresh token's replacement). It is compared as the
/// literal stored value, never re-derived from a credential, because the pointer
/// was written under whichever key issued the replacement: re-deriving it under
/// today's keys could miss it after a rotation.
/// </summary>
/// <remarks>
/// Only an entity builds one, from its own stored column, so a value computed
/// from a presented credential can never be passed where a stored pointer is
/// meant (the opposite mistake to the one <see cref="CredentialHashCandidates"/>
/// prevents).
/// </remarks>
public sealed class StoredCredentialHash : IEquatable<StoredCredentialHash>
{
    internal StoredCredentialHash(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The stored fingerprint.</summary>
    public string Value { get; }

    public bool Equals(StoredCredentialHash? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as StoredCredentialHash);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    // The fingerprint names a credential; keep it out of log lines.
    public override string ToString() => "StoredCredentialHash";
}
