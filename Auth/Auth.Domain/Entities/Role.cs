using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an authorization role that can be assigned to users.
/// Roles are scoped to applications for SSO support.
/// </summary>
public class Role : AuditableEntityBase
{
    /// <summary>
    /// Gets the ID of the application this role belongs to.
    /// Null indicates a global role applicable to all applications.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the unique role code within the application (e.g., "ADMIN", "USER").
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
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            IsActive = true,
            IsSystem = false
        };
        role.SetCreated(createdBy);
        return role;
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
