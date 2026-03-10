namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for permission implication relationships.
/// When a user has the parent permission, they also have the implied permissions.
/// </summary>
public class PermissionImplicationDto
{
    public Guid Id { get; set; }
    public Guid PermissionId { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public Guid ImpliedPermissionId { get; set; }
    public string ImpliedPermissionCode { get; set; } = string.Empty;
    public string ImpliedPermissionName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}
