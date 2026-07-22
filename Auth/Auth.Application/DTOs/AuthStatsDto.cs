namespace Auth.Application.DTOs;

/// <summary>
/// Login attempts on a single requested calendar day, split by outcome.
/// </summary>
public class DailyLoginCountDto
{
    public DateTime Date { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Failed login attempts originating from one IP address.
/// </summary>
public class IpFailureCountDto
{
    public string IpAddress { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int DistinctUsernames { get; set; }
}

/// <summary>
/// Login attempt outcomes for one application.
/// A null application means the attempt carried no application context.
/// </summary>
public class ApplicationLoginCountDto
{
    public Guid? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Login attempt outcomes attributed to one organization via the attempting user's
/// active memberships. A null organization collects unattributable attempts.
/// Multi-organization users are counted once per organization, so the sum across
/// organizations can exceed the raw attempt total.
/// </summary>
public class OrganizationLoginCountDto
{
    public Guid? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Dashboard authentication statistics over a trailing window of days.
/// Success/failure comes from login attempts; day buckets use the requested time zone.
/// </summary>
public class AuthStatsDto
{
    public int Days { get; set; }
    public List<DailyLoginCountDto> LoginsPerDay { get; set; } = [];
    public List<DailyCountDto> ActiveUsersPerDay { get; set; } = [];
    public int ActiveUsersInWindow { get; set; }
    public List<ReasonCountDto> FailureReasons { get; set; } = [];
    public int WindowSuccessCount { get; set; }
    public int WindowFailureCount { get; set; }
    public int PreviousWindowSuccessCount { get; set; }
    public int PreviousWindowFailureCount { get; set; }
    public int LockedOutNow { get; set; }
    public int LockoutEventsInWindow { get; set; }
    public List<IpFailureCountDto> TopFailingIps { get; set; } = [];
    public List<ApplicationLoginCountDto> LoginsByApplication { get; set; } = [];
    public List<OrganizationLoginCountDto> LoginsByOrganization { get; set; } = [];
}
