using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One user granted a specific permission, directly, through an organization,
/// and/or through a role containing the permission.
/// </summary>
public class PermissionUserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProfileImageUrl { get; set; }
    public UserStatus Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool ViaDirect { get; set; }
    public bool ViaOrganization { get; set; }
    public bool ViaRole { get; set; }

    /// <summary>
    /// Comma-separated names of the user's assigned roles that contain the permission.
    /// </summary>
    public string? RoleNames { get; set; }
}

/// <summary>
/// Paginated result of users granted a permission.
/// </summary>
public class PagedPermissionUsersDto
{
    public IReadOnlyList<PermissionUserDto> Users { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
