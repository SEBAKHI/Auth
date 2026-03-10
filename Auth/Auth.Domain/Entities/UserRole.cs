using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents the assignment of a role to a user.
/// Supports optional expiration for temporary role assignments.
/// </summary>
public class UserRole : EntityBase
{
    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the ID of the role.
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets the ID of the application for scoped role assignments.
    /// Null indicates a global assignment applicable to all applications.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

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

    /// <summary>
    /// Gets whether this assignment is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    private UserRole() : base()
    {
    }

    public UserRole(
        Guid id,
        Guid userId,
        Guid roleId,
        Guid? applicationId,
        DateTime assignedAt,
        Guid assignedBy,
        DateTime? expiresAt,
        bool isActive) : base(id)
    {
        UserId = userId;
        RoleId = roleId;
        ApplicationId = applicationId;
        AssignedAt = assignedAt;
        AssignedBy = assignedBy;
        ExpiresAt = expiresAt;
        IsActive = isActive;
    }

    public static UserRole Create(
        Guid userId,
        Guid roleId,
        Guid assignedBy,
        Guid? applicationId = null,
        DateTime? expiresAt = null)
    {
        return new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            ApplicationId = applicationId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = assignedBy,
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
    /// Deactivates the role assignment.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Extends the expiration of the role assignment.
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
