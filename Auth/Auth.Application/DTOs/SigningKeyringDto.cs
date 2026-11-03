namespace Auth.Application.DTOs;

/// <summary>
/// The RSA signing keyring as the administrator sees it (S12): which key signs, which is
/// staged and since when it is published, how long each older key is still accepted, and
/// what this process holds, in the secrets file and here. It never carries key material:
/// a <c>kid</c> is public (the JWKS lists it), and nothing else here is derived from a key.
/// </summary>
public record SigningKeyringDto
{
    /// <summary>Whether the signing keyring can be changed on this deployment.</summary>
    public required bool Available { get; init; }

    /// <summary>
    /// Why it cannot: <c>PlainText</c> (secrets in a settings file), <c>ExternalSource</c> (the
    /// key comes from <see cref="KeySource"/>, not the secrets file), or <c>Unreadable</c> (the
    /// ring in the secrets file cannot be used; <see cref="ErrorCode"/> says why). Null when it can.
    /// </summary>
    public string? UnavailableReason { get; init; }

    /// <summary>The setting this process took its signing key from.</summary>
    public required string KeySource { get; init; }

    /// <summary>For <c>Unreadable</c>, the code of the reason.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The key that signs, as the secrets file states it.</summary>
    public string? CurrentKid { get; init; }

    /// <summary>The key this process signs with.</summary>
    public required string ProcessCurrentKid { get; init; }

    /// <summary>The staged key, as the secrets file states it, if any.</summary>
    public string? NextKid { get; init; }

    /// <summary>The staged key this process loaded, if any.</summary>
    public string? ProcessNextKid { get; init; }

    /// <summary>
    /// Whether the secrets file holds the ring. False while the one key is the key migrated
    /// from the single signing key, before the first keyring change saves it.
    /// </summary>
    public bool? Persisted { get; init; }

    /// <summary>When a scheduled promote of the staged key becomes possible; null while no process has published it.</summary>
    public DateTime? PromoteAvailableAtUtc { get; init; }

    /// <summary>
    /// Whether the file and this process agree on the current and the staged key and on every
    /// key's state. False after a stage (until the restart that loads the key), after a change
    /// another process made, and after an emergency that could not close here at once.
    /// </summary>
    public required bool InSync { get; init; }

    /// <summary>
    /// On the answer to a change: whether this process signs and verifies under the result
    /// already. False when it applies at the next restart of the API (a stage, a discard, an
    /// emergency whose staged key this process never loaded). Null on a plain read.
    /// </summary>
    public bool? ChangeAppliedInProcess { get; init; }

    /// <summary>The keys of the file, then any key only this process still holds.</summary>
    public required IReadOnlyList<SigningKeyringEntryDto> Keys { get; init; }
}

/// <summary>One key of the RSA signing keyring, without its material.</summary>
public record SigningKeyringEntryDto
{
    /// <summary>The key's name in token headers and in the JWKS.</summary>
    public required string Kid { get; init; }

    /// <summary>Where the key stands in the file (or, for a key only this process holds, here).</summary>
    public required SigningKeyState State { get; init; }

    /// <summary>The key migrated from the single signing key: named by <c>Jwt:KeyId</c>, age unknown.</summary>
    public required bool Migrated { get; init; }

    /// <summary>When the key was staged; null for the migrated key.</summary>
    public DateTime? CreatedAtUtc { get; init; }

    /// <summary>When a process first published the staged key.</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>For a previous key, the last instant a token it signed is accepted; for a compromised one, when it was closed.</summary>
    public DateTime? VerifyUntilUtc { get; init; }

    /// <summary>The key was marked leaked by an emergency promote.</summary>
    public required bool Compromised { get; init; }

    /// <summary>Whether the secrets file holds the key.</summary>
    public required bool InFile { get; init; }

    /// <summary>Whether this process loaded the key.</summary>
    public required bool LoadedInProcess { get; init; }

    /// <summary>
    /// Whether this process accepts a token signed by the key right now. True for a compromised
    /// key only while this process has not applied the emergency: a restart closes it.
    /// </summary>
    public required bool AcceptedInProcess { get; init; }
}

/// <summary>Where a key of the RSA signing keyring stands.</summary>
public enum SigningKeyState
{
    /// <summary>It signs every new token.</summary>
    Current = 1,

    /// <summary>Staged: published and accepted, and signing once promoted.</summary>
    Next = 2,

    /// <summary>It no longer signs, and is accepted until its window ends.</summary>
    Previous = 3,

    /// <summary>It leaked: neither accepted nor published, kept as a record until retired.</summary>
    Compromised = 4
}
