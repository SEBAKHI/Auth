namespace Auth.Application.DTOs;

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
/// One application an organization may enable: switched on, open to everyone,
/// and not already enabled for it. Display fields only — this feeds a picker.
/// </summary>
/// <remarks>
/// A restricted application never appears here. It admits only the users on its
/// own access list, so no organization can enable it; the enable command refuses
/// one outright for anyone calling the API directly.
/// </remarks>
public class AvailableApplicationDto
{
    public Guid ApplicationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}
