using Auth.Domain.ReadModels.Dashboard;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Read-side repository computing dashboard aggregates directly in the database.
/// All windows are trailing periods ending now (UTC instants); calendar-day
/// buckets use the validated viewer time zone supplied by the application.
/// </summary>
public interface IDashboardStatsRepository
{
    /// <summary>
    /// Gets user totals, status mix, signups, activation funnel, dormancy and
    /// per-organization membership over the trailing window.
    /// </summary>
    Task<UserStatsSnapshot> GetUserStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets login attempt outcomes, active users, failure reasons, lockouts,
    /// top failing IPs and per-application/per-organization splits over the trailing window.
    /// </summary>
    Task<AuthStatsSnapshot> GetAuthStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets audit-event totals, the daily series, and the action and entity-type
    /// breakdowns over the trailing window.
    /// </summary>
    Task<AuditStatsSnapshot> GetAuditStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets session and refresh-token hygiene aggregates over the trailing window.
    /// </summary>
    Task<SessionStatsSnapshot> GetSessionStatsAsync(int days, CancellationToken cancellationToken);

    /// <summary>
    /// Gets per-application activity and organization/application enablements over the trailing window.
    /// </summary>
    Task<AppActivitySnapshot> GetAppActivityAsync(int days, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the expiry posture of issued API and webhook keys over a forward horizon.
    /// The only forward-looking aggregate here: every other window trails now.
    /// </summary>
    Task<CredentialStatsSnapshot> GetCredentialStatsAsync(
        int horizonDays,
        CancellationToken cancellationToken);
}
