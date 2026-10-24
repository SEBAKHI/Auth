namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for a user's role assignment.
/// </summary>
public class UserRoleDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? RoleDescription { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? ApplicationCode { get; set; }
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Whether the application this row is scoped to is deleted; its name is still
    /// given. Absent for a platform row, or when not computed.
    /// </summary>
    public bool? ApplicationIsDeleted { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }

    /// <summary>
    /// Whether the caller may remove this assignment (<c>DELETE users/{id}/roles/{roleId}</c>)
    /// by the account rules. Absent when not computed, which a client treats as allowed.
    /// </summary>
    public bool? CanRemove { get; set; }
}

/// <summary>
/// Paginated result for user roles.
/// </summary>
public class PagedUserRolesDto
{
    public IReadOnlyList<UserRoleDto> Roles { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
