namespace Auth_Lib.DTOs;

/// <summary>
/// Data transfer object for organization application subscription.
/// </summary>
public class OrganizationApplicationDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string? ApplicationDescription { get; set; }
    public string? ApplicationLogoUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime EnabledAt { get; set; }
    public Guid EnabledBy { get; set; }
    public string? EnabledByName { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? SubscriptionTier { get; set; }
    public int AssignedUserCount { get; set; }
}

/// <summary>
/// Summary of available applications for enabling.
/// </summary>
public class AvailableApplicationDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
    public string? SubscriptionTier { get; set; }
}
