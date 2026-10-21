namespace Auth.Application.Security.Keyring;

/// <summary>
/// How long a credential purpose keeps accepting fingerprints computed under a
/// key that no longer issues it: the overlap window of a rotation (KR4).
/// </summary>
/// <remarks>
/// Every method answers the last instant (inclusive) a key's fingerprint is still
/// a candidate, or null when it is not a candidate at all.
/// </remarks>
public abstract class CredentialOverlapRule
{
    /// <summary>A staged key verifies for this long after it was staged, then drops.</summary>
    public static readonly TimeSpan NextStagedWindow = TimeSpan.FromDays(30);

    /// <summary>A staged (next) key: accepted from staging until <see cref="NextStagedWindow"/> later.</summary>
    public virtual DateTime? NextAcceptedUntil(HmacKey next) =>
        next.CreatedAtUtc is { } stagedAt ? stagedAt + NextStagedWindow : null;

    /// <summary>A previous key (demoted, verify-only), never key 1.</summary>
    public abstract DateTime? PreviousAcceptedUntil(HmacKey previous);

    /// <summary>Key 1's legacy form once the keyring issues under purpose subkeys.</summary>
    public abstract DateTime? LegacyAcceptedUntil(HmacKeyring ring, HmacKey legacyKey);
}

/// <summary>
/// The rule of every purpose whose credential has a bounded lifetime: an old key
/// is accepted for that lifetime after it stopped issuing, plus clock slack, and
/// never past its own <see cref="HmacKey.VerifyUntilUtc"/>. A compromised key is
/// accepted for nothing.
/// </summary>
public sealed class PurposeLimitedOverlap : CredentialOverlapRule
{
    /// <summary>Clock slack added to every bounded window (KR4).</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(300);

    /// <summary>The legacy form is never accepted longer than this after the cutover, whatever the purpose's lifetime.</summary>
    public static readonly TimeSpan LegacyCap = TimeSpan.FromDays(30);

    public PurposeLimitedOverlap(TimeSpan limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
        Limit = limit;
    }

    /// <summary>The longest a credential of the purpose can live, as the code can configure it today.</summary>
    public TimeSpan Limit { get; }

    public override DateTime? NextAcceptedUntil(HmacKey next) =>
        next.Compromised ? null : base.NextAcceptedUntil(next);

    public override DateTime? PreviousAcceptedUntil(HmacKey previous)
    {
        if (previous.Compromised || previous.DemotedAtUtc is not { } demotedAt)
        {
            return null;
        }

        return Earliest(demotedAt + Limit + Slack, previous.VerifyUntilUtc);
    }

    public override DateTime? LegacyAcceptedUntil(HmacKeyring ring, HmacKey legacyKey)
    {
        if (legacyKey.Compromised || ring.LegacyCutoverUtc is not { } cutover)
        {
            return null;
        }

        var window = Limit < LegacyCap ? Limit : LegacyCap;
        var until = Earliest(cutover + window + Slack, ring.LegacyVerifyUntilUtc);
        return Earliest(until, legacyKey.VerifyUntilUtc);
    }

    private static DateTime Earliest(DateTime bound, DateTime? other) =>
        other is { } value && value < bound ? value : bound;
}

/// <summary>
/// The webhook-key rule (owner decision §11 q2, 2026-09-29): a webhook key has no
/// lifetime, so a key computed under an old, legacy or compromised key is
/// accepted for <see cref="WebhookKeyGraceDays"/> days after that key stopped
/// issuing, with no bound from <see cref="HmacKey.VerifyUntilUtc"/>; a key idle
/// past that is re-issued by its administrator. A leaked HMAC key alone cannot
/// forge a random webhook key, which is why compromise does not close it.
/// </summary>
public sealed class WebhookKeyGraceOverlap : CredentialOverlapRule
{
    /// <summary>A code constant, not a setting (C9).</summary>
    public const int WebhookKeyGraceDays = 90;

    private static readonly TimeSpan Grace = TimeSpan.FromDays(WebhookKeyGraceDays);

    public override DateTime? PreviousAcceptedUntil(HmacKey previous) =>
        previous.DemotedAtUtc is { } demotedAt ? demotedAt + Grace : null;

    public override DateTime? LegacyAcceptedUntil(HmacKeyring ring, HmacKey legacyKey) =>
        ring.LegacyCutoverUtc is { } cutover ? cutover + Grace : null;
}
