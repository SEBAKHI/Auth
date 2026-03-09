using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents the assignment of a permission to a role.
/// </summary>
public class RolePermission : EntityBase
{
    /// <summary>
    /// Gets the ID of the role.
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets the ID of the permission.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the permission was granted.
    /// </summary>
    public DateTime GrantedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who granted the permission.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    private RolePermission() : base()
    {
    }

    public RolePermission(
        Guid id,
        Guid roleId,
        Guid permissionId,
        DateTime grantedAt,
        Guid grantedBy) : base(id)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
    }

    public static RolePermission Create(
        Guid roleId,
        Guid permissionId,
        Guid grantedBy)
    {
        return new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = grantedBy
        };
    }
}
