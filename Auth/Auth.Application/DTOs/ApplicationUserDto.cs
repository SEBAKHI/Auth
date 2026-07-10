using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One user holding an active role assignment scoped to an application,
/// either directly or through an organization.
/// </summary>
public class ApplicationUserDto
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

    /// <summary>
    /// Comma-separated names of the application's roles held by the user.
    /// </summary>
    public string? RoleNames { get; set; }
}

/// <summary>
/// Paginated result of users under an application.
/// </summary>
public class PagedApplicationUsersDto
{
    public IReadOnlyList<ApplicationUserDto> Users { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
