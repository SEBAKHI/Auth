namespace Auth_Lib.DTOs;

/// <summary>
/// Data transfer object for role information.
/// </summary>
public class RoleDto
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public byte Level { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public IReadOnlyList<string> Permissions { get; set; } = [];
}

/// <summary>
/// Paginated result for roles.
/// </summary>
public class PagedRolesDto
{
    public IReadOnlyList<RoleDto> Roles { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
