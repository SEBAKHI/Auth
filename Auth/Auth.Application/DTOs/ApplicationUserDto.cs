using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// One user attached to an application: invited on its access list, holding an
/// application-scoped role directly, and/or holding one through an organization.
/// </summary>
/// <remarks>
/// A roster, not an admission list. Only the invitation lets someone into a
/// restricted application, and an open one admits people who never appear here.
/// </remarks>
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

    /// <summary>
    /// Why the user appears on this roster: "grant" (invited), "direct" (an
    /// application-scoped role), "organization" (a role through an organization),
    /// or "multiple" when more than one applies.
    /// </summary>
    public string AccessSource { get; set; } = string.Empty;
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
