namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Aggregated audit-event statistics over a trailing window of days.
/// Calendar days use the requested time zone; stored timestamps remain UTC.
/// </summary>
/// <remarks>
/// The AuditLogs table records what happened, not whether it succeeded: it has no
/// outcome column and no action-type column, so this snapshot deliberately carries
/// no success/failure split. Grouping is by the columns that actually exist —
/// <c>Action</c> and <c>EntityType</c>.
/// </remarks>
public sealed record AuditStatsSnapshot
{
    /// <summary>Events recorded inside the window.</summary>
    public required int TotalInWindow { get; init; }

    /// <summary>Events recorded inside the window immediately preceding this one.</summary>
    public required int PreviousWindowTotal { get; init; }

    /// <summary>Events per requested calendar day.</summary>
    public required IReadOnlyList<DailyCount> EventsPerDay { get; init; }

    /// <summary>Events inside the window grouped by action, most frequent first.</summary>
    public required IReadOnlyList<ReasonCount> TopActions { get; init; }

    /// <summary>
    /// Events inside the window grouped by affected entity type, most frequent
    /// first. Entries with no entity type are collected under 'unknown'.
    /// </summary>
    public required IReadOnlyList<ReasonCount> ByEntityType { get; init; }
}
