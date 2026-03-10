namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for permission information.
/// </summary>
public class PermissionDto
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public byte Level { get; set; }
    public bool IsWildcard { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
