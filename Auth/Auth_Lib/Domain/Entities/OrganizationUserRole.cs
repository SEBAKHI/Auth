using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an app-level role assignment within an organization.
/// This links a user to an application-specific role within an organization context.
/// Example: User X has "Data Transfer Editor" role in Organization Y for Application Z.
/// </summary>
public class OrganizationUserRole : AuditableEntityBase
{
    /// <summary>
    /// Gets the ID of the organization.
    /// </summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the ID of the application.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the ID of the role (an app-specific role like "Data Transfer Editor").
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets whether this role assignment is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the role was assigned.
    /// </summary>
    public DateTime AssignedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who assigned the role.
    /// </summary>
    public Guid AssignedBy { get; private set; }

    /// <summary>
    /// Gets the optional UTC timestamp when the assignment expires.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    private OrganizationUserRole() : base()
    {
    }

    public OrganizationUserRole(
        Guid id,
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        bool isActive,
        DateTime assignedAt,
        Guid assignedBy,
        DateTime? expiresAt,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        OrganizationId = organizationId;
        UserId = userId;
        ApplicationId = applicationId;
        RoleId = roleId;
        IsActive = isActive;
        AssignedAt = assignedAt;
        AssignedBy = assignedBy;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates a new app-level role assignment within an organization.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="applicationId">The application ID</param>
    /// <param name="roleId">The app-specific role ID</param>
    /// <param name="assignedBy">Who assigned the role</param>
    /// <param name="expiresAt">Optional expiration date</param>
    /// <returns>New OrganizationUserRole instance</returns>
    public static OrganizationUserRole Create(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        Guid assignedBy,
        DateTime? expiresAt = null)
    {
        var assignment = new OrganizationUserRole
        {
            OrganizationId = organizationId,
            UserId = userId,
            ApplicationId = applicationId,
            RoleId = roleId,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = assignedBy,
            ExpiresAt = expiresAt
        };
        assignment.SetCreated(assignedBy);
        return assignment;
    }

    /// <summary>
    /// Checks if the assignment is valid (active and not expired).
    /// </summary>
    public bool IsValid()
    {
        return IsActive && !IsExpired();
    }

    /// <summary>
    /// Checks if the assignment has expired.
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    /// <summary>
    /// Extends the expiration of the assignment.
    /// </summary>
    public void ExtendExpiration(DateTime newExpiresAt, Guid modifiedBy)
    {
        ExpiresAt = newExpiresAt;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Removes the expiration (makes permanent).
    /// </summary>
    public void MakePermanent(Guid modifiedBy)
    {
        ExpiresAt = null;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Deactivates the role assignment.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Reactivates the role assignment.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }
}
