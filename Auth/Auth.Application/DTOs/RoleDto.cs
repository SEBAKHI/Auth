namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for role information.
/// </summary>
public class RoleDto
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }

    /// <summary>Name of the owning application; null for system-wide roles or when unresolved.</summary>
    public string? ApplicationName { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public byte Level { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    /// <summary>Display name of the last modifying user; null when unresolved.</summary>
    public string? ModifiedByName { get; set; }

    public IReadOnlyList<string> Permissions { get; set; } = [];

    /// <summary>
    /// Which of <see cref="Permissions"/> the caller may remove now
    /// (<c>DELETE roles/{id}/permissions/{permissionId}</c>), by the rules the removal
    /// enforces beyond its <c>roles:update</c> permission, which the client checks
    /// itself. Absent when not computed for a caller (every response but the role
    /// read), which a client treats as every code removable.
    /// </summary>
    public IReadOnlyList<string>? RemovablePermissions { get; set; }

    /// <summary>
    /// Whether the caller may grant this role now, by the no-amplification rule the
    /// grant enforces beyond its endpoint permission: the caller covers its codes, plus
    /// <c>applications:update</c> when it belongs to an application. Absent means not
    /// computed (no caller on the request); a client treats absent as grantable.
    /// </summary>
    public bool? CanGrant { get; set; }
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
