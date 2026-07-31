namespace Auth.Application.DTOs;

/// <summary>
/// Aggregated audit-event statistics over a trailing window of days.
/// </summary>
/// <remarks>
/// Computed in the database over the whole table. The AuditLogs table records what
/// happened rather than whether it succeeded — it has no outcome column and no
/// action-type column — so this contract carries no success/failure split, and
/// groups only by <c>Action</c> and <c>EntityType</c>. Entity types are collected
/// under 'unknown' when absent. Per-property semantics live on
/// <see cref="Auth.Domain.ReadModels.Dashboard.AuditStatsSnapshot"/>; the shape here
/// stays plain to match the other dashboard DTOs.
/// </remarks>
public class AuditStatsDto
{
    public int Days { get; set; }
    public int TotalInWindow { get; set; }
    public int PreviousWindowTotal { get; set; }
    public List<DailyCountDto> EventsPerDay { get; set; } = [];
    public List<ReasonCountDto> TopActions { get; set; } = [];
    public List<ReasonCountDto> ByEntityType { get; set; } = [];
}
