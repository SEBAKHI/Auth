using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an individual permission grant to a user within an organization for a specific application.
/// This allows granular permission assignment without using roles.
/// Example: User X has "data-transfer:export" permission in Organization Y for Application Z.
/// </summary>
public class OrganizationUserPermission : AuditableEntityBase
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
    /// Gets the ID of the permission being granted.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets whether this permission grant is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the permission was granted.
    /// </summary>
    public DateTime GrantedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who granted the permission.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    /// <summary>
    /// Gets the optional UTC timestamp when the grant expires.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    private OrganizationUserPermission() : base()
    {
    }

    public OrganizationUserPermission(
        Guid id,
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        bool isActive,
        DateTime grantedAt,
        Guid grantedBy,
        DateTime? expiresAt,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        OrganizationId = organizationId;
        UserId = userId;
        ApplicationId = applicationId;
        PermissionId = permissionId;
        IsActive = isActive;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates a new individual permission grant within an organization.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="applicationId">The application ID</param>
    /// <param name="permissionId">The permission ID</param>
    /// <param name="grantedBy">Who granted the permission</param>
    /// <param name="expiresAt">Optional expiration date</param>
    /// <returns>New OrganizationUserPermission instance</returns>
    public static OrganizationUserPermission Create(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        Guid grantedBy,
        DateTime? expiresAt = null)
    {
        var grant = new OrganizationUserPermission
        {
            OrganizationId = organizationId,
            UserId = userId,
            ApplicationId = applicationId,
            PermissionId = permissionId,
            IsActive = true,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = grantedBy,
            ExpiresAt = expiresAt
        };
        grant.SetCreated(grantedBy);
        return grant;
    }

    /// <summary>
    /// Checks if the grant is valid (active and not expired).
    /// </summary>
    public bool IsValid()
    {
        return IsActive && !IsExpired();
    }

    /// <summary>
    /// Checks if the grant has expired.
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    /// <summary>
    /// Extends the expiration of the grant.
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
    /// Deactivates the permission grant.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Reactivates the permission grant.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }
}
