namespace Auth_Lib.Application.DTOs;

/// <summary>
/// Data transfer object for organization information.
/// </summary>
public class OrganizationDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? Website { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string? OwnerEmail { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int EnabledAppCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Detailed organization information including members and applications.
/// </summary>
public class OrganizationDetailDto : OrganizationDto
{
    public IReadOnlyList<OrganizationMemberDto> Members { get; set; } = [];
    public IReadOnlyList<OrganizationApplicationDto> EnabledApplications { get; set; } = [];
}

/// <summary>
/// Summary information for organization listing.
/// </summary>
public class OrganizationSummaryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public string? UserRole { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>
/// Paginated result for organizations.
/// </summary>
public class PagedOrganizationsDto
{
    public IReadOnlyList<OrganizationDto> Organizations { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
