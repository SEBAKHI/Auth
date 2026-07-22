namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Number of non-deleted users in a given account status.
/// </summary>
public sealed record UserStatusCount(byte Status, int Count);

/// <summary>
/// Active member count of one organization.
/// </summary>
public sealed record OrganizationUserCount(
    Guid OrganizationId,
    string OrganizationName,
    bool IsAutoCreated,
    int Count);

/// <summary>
/// Aggregated user statistics over a trailing window of days.
/// All counts exclude soft-deleted users; calendar-day buckets use the requested time zone.
/// </summary>
public sealed record UserStatsSnapshot
{
    /// <summary>Total non-deleted users.</summary>
    public required int TotalUsers { get; init; }

    /// <summary>Users grouped by account status (1=Active, 2=Inactive, 3=Locked, 4=PendingVerification).</summary>
    public required IReadOnlyList<UserStatusCount> ByStatus { get; init; }

    /// <summary>Active users with two-factor authentication enabled.</summary>
    public required int MfaEnabled { get; init; }

    /// <summary>Active users (denominator for MFA adoption).</summary>
    public required int ActiveUsers { get; init; }

    /// <summary>Users created inside the window.</summary>
    public required int NewInWindow { get; init; }

    /// <summary>Users created per requested calendar day inside the window.</summary>
    public required IReadOnlyList<DailyCount> SignupsPerDay { get; init; }

    /// <summary>Window cohort: users created inside the window.</summary>
    public required int CohortCreated { get; init; }

    /// <summary>Window cohort: created inside the window and email-confirmed.</summary>
    public required int CohortEmailConfirmed { get; init; }

    /// <summary>Window cohort: created inside the window and logged in at least once.</summary>
    public required int CohortLoggedIn { get; init; }

    /// <summary>Active users whose last activity signal (last login, or creation when never
    /// logged in) is more than 30 days old.</summary>
    public required int DormantOver30Days { get; init; }

    /// <summary>Active users whose last activity signal is more than 60 days old.</summary>
    public required int DormantOver60Days { get; init; }

    /// <summary>Active users whose last activity signal is more than 90 days old.</summary>
    public required int DormantOver90Days { get; init; }

    /// <summary>Active users who have never logged in.</summary>
    public required int NeverLoggedIn { get; init; }

    /// <summary>Active membership count of the ten largest organizations.</summary>
    public required IReadOnlyList<OrganizationUserCount> UsersByOrganization { get; init; }

    /// <summary>Total active memberships across all organizations (lets callers derive an
    /// "Other" bucket beyond the listed organizations).</summary>
    public required int TotalActiveMemberships { get; init; }
}
