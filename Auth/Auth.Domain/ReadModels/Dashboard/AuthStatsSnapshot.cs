namespace Auth.Domain.ReadModels.Dashboard;

/// <summary>
/// Successful and failed login attempts on a single UTC calendar day.
/// </summary>
public sealed record DailyLoginCount(DateTime Date, int SuccessCount, int FailureCount);

/// <summary>
/// Failed login attempts originating from one IP address.
/// </summary>
public sealed record IpFailureCount(string IpAddress, int FailureCount, int DistinctUsernames);

/// <summary>
/// Login attempt outcome split for one application.
/// A null application means the attempt carried no application context.
/// </summary>
public sealed record ApplicationLoginCount(
    Guid? ApplicationId,
    string? ApplicationName,
    int SuccessCount,
    int FailureCount);

/// <summary>
/// Login attempt outcome split attributed to one organization via the
/// attempting user's active memberships. A null organization collects
/// attempts that could not be attributed (unknown user or no membership).
/// Users belonging to several organizations are counted once per organization,
/// so the sum across organizations can exceed the raw attempt total.
/// </summary>
public sealed record OrganizationLoginCount(
    Guid? OrganizationId,
    string? OrganizationName,
    int SuccessCount,
    int FailureCount);

/// <summary>
/// Aggregated authentication statistics over a trailing window of days.
/// All dates are UTC calendar days; success/failure comes from LoginAttempts.IsSuccessful.
/// </summary>
public sealed record AuthStatsSnapshot
{
    /// <summary>Login attempts per UTC day, split by outcome.</summary>
    public required IReadOnlyList<DailyLoginCount> LoginsPerDay { get; init; }

    /// <summary>Distinct users with at least one successful login per UTC day.</summary>
    public required IReadOnlyList<DailyCount> ActiveUsersPerDay { get; init; }

    /// <summary>Distinct users with at least one successful login inside the window.</summary>
    public required int ActiveUsersInWindow { get; init; }

    /// <summary>Failed attempts inside the window grouped by failure reason.</summary>
    public required IReadOnlyList<ReasonCount> FailureReasons { get; init; }

    /// <summary>Successful attempts inside the window.</summary>
    public required int WindowSuccessCount { get; init; }

    /// <summary>Failed attempts inside the window.</summary>
    public required int WindowFailureCount { get; init; }

    /// <summary>Successful attempts inside the window immediately preceding this one.</summary>
    public required int PreviousWindowSuccessCount { get; init; }

    /// <summary>Failed attempts inside the window immediately preceding this one.</summary>
    public required int PreviousWindowFailureCount { get; init; }

    /// <summary>Users currently locked out (LockoutEndUtc in the future).</summary>
    public required int LockedOutNow { get; init; }

    /// <summary>Attempts rejected with reason 'account_locked' inside the window.</summary>
    public required int LockoutEventsInWindow { get; init; }

    /// <summary>IP addresses with the most failed attempts inside the window.</summary>
    public required IReadOnlyList<IpFailureCount> TopFailingIps { get; init; }

    /// <summary>Attempt outcomes inside the window grouped by application.</summary>
    public required IReadOnlyList<ApplicationLoginCount> LoginsByApplication { get; init; }

    /// <summary>Attempt outcomes inside the window attributed to organizations by membership.</summary>
    public required IReadOnlyList<OrganizationLoginCount> LoginsByOrganization { get; init; }
}
