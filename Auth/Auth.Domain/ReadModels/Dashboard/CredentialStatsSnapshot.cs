namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Expiry posture of one credential family right now. Unlike every other dashboard
/// snapshot the window here runs forward, not behind: the question is what stops
/// working next, not what happened last.
/// </summary>
public sealed record CredentialExpiryBucket
{
    /// <summary>
    /// Live credentials that stop authenticating inside the horizon. Every one of
    /// them, including a rotation's outgoing key during its grace window: nothing
    /// in the schema tells a replacement apart from an unrelated second key, and
    /// suppressing on a guess loses real warnings.
    /// </summary>
    public required int ExpiringCount { get; init; }

    /// <summary>Earliest expiry among the counted credentials; null when none are counted.</summary>
    public required DateTime? SoonestExpiresAt { get; init; }

    /// <summary>Every live credential of this family, the denominator for the count above.</summary>
    public required int TotalActive { get; init; }
}

/// <summary>
/// Expiry posture of the long-lived credentials callers authenticate with.
/// Deliberately platform-wide: neither ApiKeys nor WebhookKeys carries a tenant
/// column, and a key belongs to an Application, not to an Organization.
/// </summary>
public sealed record CredentialStatsSnapshot
{
    /// <summary>Days ahead the expiry counts look.</summary>
    public required int HorizonDays { get; init; }

    /// <summary>API keys (dbo.ApiKeys).</summary>
    public required CredentialExpiryBucket ApiKeys { get; init; }

    /// <summary>Webhook signing keys (dbo.WebhookKeys).</summary>
    public required CredentialExpiryBucket WebhookKeys { get; init; }
}
