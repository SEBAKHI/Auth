using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a direct permission assignment to a user (bypassing roles).
/// </summary>
public class UserPermission : EntityBase
{
    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the ID of the permission.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets the ID of the application for scoped permission assignments.
    /// Null indicates a global assignment applicable to all applications.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the permission was granted.
    /// </summary>
    public DateTime GrantedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who granted the permission.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    /// <summary>
    /// Gets the optional UTC timestamp when the assignment expires.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>
    /// Gets whether this assignment is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    private UserPermission() : base()
    {
    }

    public UserPermission(
        Guid id,
        Guid userId,
        Guid permissionId,
        Guid? applicationId,
        DateTime grantedAt,
        Guid grantedBy,
        DateTime? expiresAt,
        bool isActive) : base(id)
    {
        UserId = userId;
        PermissionId = permissionId;
        ApplicationId = applicationId;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
        ExpiresAt = expiresAt;
        IsActive = isActive;
    }

    public static UserPermission Create(
        Guid userId,
        Guid permissionId,
        Guid grantedBy,
        Guid? applicationId = null,
        DateTime? expiresAt = null)
    {
        return new UserPermission
        {
            UserId = userId,
            PermissionId = permissionId,
            ApplicationId = applicationId,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = grantedBy,
            ExpiresAt = expiresAt,
            IsActive = true
        };
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
    /// Deactivates the permission assignment.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Extends the expiration.
    /// </summary>
    public void ExtendExpiration(DateTime newExpiresAt)
    {
        ExpiresAt = newExpiresAt;
    }

    /// <summary>
    /// Removes the expiration (makes permanent).
    /// </summary>
    public void MakePermanent()
    {
        ExpiresAt = null;
    }
}
