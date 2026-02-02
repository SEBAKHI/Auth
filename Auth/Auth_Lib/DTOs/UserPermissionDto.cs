namespace Auth_Lib.DTOs;

/// <summary>
/// Data transfer object for a user's direct permission grant.
/// </summary>
public class UserPermissionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string? PermissionDescription { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? ApplicationCode { get; set; }
    public string? ApplicationName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// Paginated result for user permissions.
/// </summary>
public class PagedUserPermissionsDto
{
    public IReadOnlyList<UserPermissionDto> Permissions { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
