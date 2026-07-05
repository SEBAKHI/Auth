using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One user holding an active assignment of a specific role.
/// </summary>
public class RoleUserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public UserStatus Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// How the role is assigned: "direct", "organization", or "both".
    /// </summary>
    public string AssignmentSource { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated names of the organizations the role is assigned through.
    /// </summary>
    public string? OrganizationNames { get; set; }
}

/// <summary>
/// Paginated result of users assigned a role.
/// </summary>
public class PagedRoleUsersDto
{
    public IReadOnlyList<RoleUserDto> Users { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
