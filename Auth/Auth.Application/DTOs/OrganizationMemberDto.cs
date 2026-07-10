namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for organization member information.
/// </summary>
public class OrganizationMemberDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime JoinedAt { get; set; }
    public Guid InvitedBy { get; set; }
    public string? InvitedByName { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Detailed member information including app-level roles and permissions.
/// </summary>
public class OrganizationMemberDetailDto : OrganizationMemberDto
{
    public IReadOnlyList<OrganizationMemberAppRoleDto> AppRoles { get; set; } = [];
    public IReadOnlyList<OrganizationMemberPermissionDto> DirectPermissions { get; set; } = [];
}

/// <summary>
/// App-level role assignment within an organization.
/// </summary>
public class OrganizationMemberAppRoleDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public Guid AssignedBy { get; set; }
    public string? AssignedByName { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Individual permission grant within an organization.
/// </summary>
public class OrganizationMemberPermissionDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public Guid PermissionId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public Guid GrantedBy { get; set; }
    public string? GrantedByName { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Paginated result for organization members.
/// </summary>
public class PagedOrganizationMembersDto
{
    public IReadOnlyList<OrganizationMemberDto> Members { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
