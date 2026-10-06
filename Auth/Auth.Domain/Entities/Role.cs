using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an authorization role that can be assigned to users.
/// Roles are scoped to applications for SSO support.
/// </summary>
public class Role : AggregateRoot
{
    /// <summary>
    /// Gets the ID of the application this role belongs to.
    /// Null indicates a global role applicable to all applications.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the unique role code within the application (e.g., "admin", "support-agent").
    /// Stored lowercase.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name of the role.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of the role.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets whether this role is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets whether this is a system role (cannot be deleted).
    /// </summary>
    public bool IsSystem { get; private set; }

    private Role() : base()
    {
    }

    public Role(
        Guid id,
        Guid? applicationId,
        string code,
        string name,
        string? description,
        bool isActive,
        bool isSystem,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        ApplicationId = applicationId;
        Code = code;
        Name = name;
        Description = description;
        IsActive = isActive;
        IsSystem = isSystem;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    public static Role Create(
        Guid? applicationId,
        string code,
        string name,
        string? description,
        Guid createdBy)
    {
        var role = new Role
        {
            ApplicationId = applicationId,
            // Lowercase, like every seeded role. Invariant, never ToLower():
            // under a Turkish request culture that turns "I" into a dotless "ı".
            Code = code.ToLowerInvariant(),
            Name = name,
            Description = description,
            IsActive = true,
            IsSystem = false
        };
        role.SetCreated(createdBy);
        return role;
    }

    /// <summary>
    /// Confirms that <paramref name="permission"/> may be part of this role: both
    /// belong to the same application, or both to the platform (no application).
    /// </summary>
    /// <remarks>
    /// Everything a role holds reaches every token minted from it, and an
    /// application token's organization permissions take the role's whole set.
    /// A platform code, or the global <c>*</c>, added to an application's role
    /// would reach every holder of that role inside the application. Neither the
    /// role's nor the permission's application changes after creation, so the
    /// check when a permission is added is enough.
    /// </remarks>
    public ErrorOr<Success> EnsureCanHold(Permission permission)
    {
        return permission.ApplicationId == ApplicationId
            ? Result.Success
            : RoleErrors.PermissionNotForApplication;
    }

    public void Update(
        string name,
        string? description,
        Guid modifiedBy)
    {
        Name = name;
        Description = description;
        SetModified(modifiedBy);
    }

    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }
}
