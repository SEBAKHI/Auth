namespace Auth.Application.DTOs;

/// <summary>
/// The HMAC keyring as the administrator sees it (S11): which key issues, which key is
/// staged, and how long each older key keeps verifying, in the secrets file and in this
/// process. It never carries key material: no member of this type or of its entries
/// holds a key or anything derived from one.
/// </summary>
public record HmacKeyringDto
{
    /// <summary>The key that issues, as the secrets file states it.</summary>
    public required int CurrentKeyId { get; init; }

    /// <summary>The key this process issues under.</summary>
    public required int ProcessCurrentKeyId { get; init; }

    /// <summary>The staged key, as the secrets file states it, if any.</summary>
    public int? NextKeyId { get; init; }

    /// <summary>The staged key this process verifies under, if any.</summary>
    public int? ProcessNextKeyId { get; init; }

    /// <summary>
    /// Whether the file and this process agree on the current key, the staged key and
    /// the cutover. False after another worker changed the file, or a failed swap: the
    /// process catches up at its next start.
    /// </summary>
    public required bool InSync { get; init; }

    /// <summary>When issuing moved from key 1's legacy form to purpose subkeys; null before the first rotation.</summary>
    public DateTime? LegacyCutoverUtc { get; init; }

    /// <summary>The keys, by id.</summary>
    public required IReadOnlyList<HmacKeyringEntryDto> Keys { get; init; }
}

/// <summary>One key of the HMAC keyring, without its material.</summary>
public record HmacKeyringEntryDto
{
    /// <summary>The key's id. Key 1 is the key the deployment started with.</summary>
    public required int Id { get; init; }

    /// <summary>Where the key stands: current, staged (next) or previous.</summary>
    public required HmacKeyState State { get; init; }

    /// <summary>When the key was staged; null for key 1 when its age is unknown.</summary>
    public DateTime? CreatedAtUtc { get; init; }

    /// <summary>When the key stopped issuing; null while it is current or staged.</summary>
    public DateTime? DemotedAtUtc { get; init; }

    /// <summary>
    /// The last instant the key verifies any credential other than a webhook key; null
    /// for the current key, and for a key that verifies none any more.
    /// </summary>
    public DateTime? VerifyUntilUtc { get; init; }

    /// <summary>The last instant a webhook key stored under this key still validates; null when none does.</summary>
    public DateTime? WebhookGraceUntilUtc { get; init; }

    /// <summary>The key was marked leaked by an emergency rotation.</summary>
    public required bool Compromised { get; init; }

    /// <summary>Live webhook keys whose stored fingerprint was computed under this key.</summary>
    public required int WebhookKeyCount { get; init; }

    /// <summary>Whether this process holds the key, with the same material as the file.</summary>
    public required bool LoadedInProcess { get; init; }
}

/// <summary>Where a key of the HMAC keyring stands.</summary>
public enum HmacKeyState
{
    /// <summary>It issues every new fingerprint.</summary>
    Current = 1,

    /// <summary>Staged: it verifies, and issues once promoted.</summary>
    Next = 2,

    /// <summary>It no longer issues, and verifies until its window ends.</summary>
    Previous = 3
}
