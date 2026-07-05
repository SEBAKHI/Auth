namespace Auth.Application.DTOs;

/// <summary>
/// Number of users in one account status.
/// </summary>
public class UserStatusCountDto
{
    public byte Status { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Active member count of one organization.
/// </summary>
public class OrganizationUserCountDto
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public bool IsAutoCreated { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Dashboard user statistics over a trailing window of days.
/// All counts exclude soft-deleted users; day buckets are UTC calendar days.
/// </summary>
public class UserStatsDto
{
    public int Days { get; set; }
    public int TotalUsers { get; set; }
    public List<UserStatusCountDto> ByStatus { get; set; } = [];
    public int ActiveUsers { get; set; }
    public int MfaEnabled { get; set; }
    public int NewInWindow { get; set; }
    public List<DailyCountDto> SignupsPerDay { get; set; } = [];
    public int CohortCreated { get; set; }
    public int CohortEmailConfirmed { get; set; }
    public int CohortLoggedIn { get; set; }
    public int DormantOver30Days { get; set; }
    public int DormantOver60Days { get; set; }
    public int DormantOver90Days { get; set; }
    public int NeverLoggedIn { get; set; }
    public List<OrganizationUserCountDto> UsersByOrganization { get; set; } = [];
    public int TotalActiveMemberships { get; set; }
}
