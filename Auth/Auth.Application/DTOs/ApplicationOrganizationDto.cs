namespace Auth.Application.DTOs;

/// <summary>
/// One organization that has an application enabled, with the enablement link
/// details. Inactive links are included so admins can see disabled tenants.
/// </summary>
public class ApplicationOrganizationDto
{
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool OrganizationIsActive { get; set; }
    public bool IsActive { get; set; }
    public DateTime EnabledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>
/// Paginated result of organizations that enabled an application.
/// </summary>
public class PagedApplicationOrganizationsDto
{
    public IReadOnlyList<ApplicationOrganizationDto> Organizations { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
