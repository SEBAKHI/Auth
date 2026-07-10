namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for permission information.
/// </summary>
public class PermissionDto
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }

    /// <summary>Name of the owning application; null for system-wide permissions or when unresolved.</summary>
    public string? ApplicationName { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public byte Level { get; set; }
    public bool IsWildcard { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    /// <summary>Display name of the last modifying user; null when unresolved.</summary>
    public string? ModifiedByName { get; set; }
}
