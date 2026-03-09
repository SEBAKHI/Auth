using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a permission implication relationship.
/// When a user has the parent permission, they also implicitly have the implied permission.
/// Example: crm:leads:write implies crm:leads:read (you must see to edit)
/// </summary>
public class PermissionImplication : EntityBase
{
    /// <summary>
    /// Gets the ID of the permission that implies another permission.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets the ID of the permission that is implied.
    /// </summary>
    public Guid ImpliedPermissionId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the implication was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who created the implication.
    /// </summary>
    public Guid CreatedBy { get; private set; }

    private PermissionImplication() : base()
    {
    }

    public PermissionImplication(
        Guid id,
        Guid permissionId,
        Guid impliedPermissionId,
        DateTime createdAt,
        Guid createdBy) : base(id)
    {
        PermissionId = permissionId;
        ImpliedPermissionId = impliedPermissionId;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public static PermissionImplication Create(
        Guid permissionId,
        Guid impliedPermissionId,
        Guid createdBy)
    {
        return new PermissionImplication
        {
            PermissionId = permissionId,
            ImpliedPermissionId = impliedPermissionId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }
}
