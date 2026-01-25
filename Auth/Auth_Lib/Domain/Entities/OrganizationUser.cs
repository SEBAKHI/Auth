using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a user's membership in an organization.
/// Includes the organization-level role (org-owner, org-admin, org-member).
/// </summary>
public class OrganizationUser : AuditableEntityBase
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
    /// Gets the ID of the organization-level role.
    /// This determines what the user can do within the organization itself
    /// (e.g., org-owner, org-admin, org-member).
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets whether this membership is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the user joined the organization.
    /// </summary>
    public DateTime JoinedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who invited/added this member.
    /// </summary>
    public Guid InvitedBy { get; private set; }

    /// <summary>
    /// Gets the optional UTC timestamp when the membership expires.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    private OrganizationUser() : base()
    {
    }

    public OrganizationUser(
        Guid id,
        Guid organizationId,
        Guid userId,
        Guid roleId,
        bool isActive,
        DateTime joinedAt,
        Guid invitedBy,
        DateTime? expiresAt,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
        IsActive = isActive;
        JoinedAt = joinedAt;
        InvitedBy = invitedBy;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates a new organization membership.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="roleId">The organization-level role ID</param>
    /// <param name="invitedBy">Who invited/added this user</param>
    /// <param name="expiresAt">Optional expiration date</param>
    /// <returns>New OrganizationUser instance</returns>
    public static OrganizationUser Create(
        Guid organizationId,
        Guid userId,
        Guid roleId,
        Guid invitedBy,
        DateTime? expiresAt = null)
    {
        var membership = new OrganizationUser
        {
            OrganizationId = organizationId,
            UserId = userId,
            RoleId = roleId,
            IsActive = true,
            JoinedAt = DateTime.UtcNow,
            InvitedBy = invitedBy,
            ExpiresAt = expiresAt
        };
        membership.SetCreated(invitedBy);
        return membership;
    }

    /// <summary>
    /// Checks if the membership is valid (active and not expired).
    /// </summary>
    public bool IsValid()
    {
        return IsActive && !IsExpired();
    }

    /// <summary>
    /// Checks if the membership has expired.
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    /// <summary>
    /// Updates the organization-level role.
    /// </summary>
    /// <param name="newRoleId">The new role ID</param>
    /// <param name="modifiedBy">Who made the change</param>
    public void UpdateRole(Guid newRoleId, Guid modifiedBy)
    {
        RoleId = newRoleId;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Extends the expiration of the membership.
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
    /// Deactivates the membership.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Reactivates the membership.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }
}
