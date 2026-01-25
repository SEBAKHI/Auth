using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an organization's subscription/enablement of an application.
/// When an organization enables an app, its members can access that app
/// (subject to their individual permissions).
/// </summary>
public class OrganizationApplication : AuditableEntityBase
{
    /// <summary>
    /// Gets the ID of the organization.
    /// </summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Gets the ID of the application.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets whether this subscription is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the app was enabled for the organization.
    /// </summary>
    public DateTime EnabledAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who enabled the app.
    /// </summary>
    public Guid EnabledBy { get; private set; }

    /// <summary>
    /// Gets the optional UTC timestamp when the subscription expires.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the optional subscription tier (e.g., "free", "pro", "enterprise").
    /// </summary>
    public string? SubscriptionTier { get; private set; }

    private OrganizationApplication() : base()
    {
    }

    public OrganizationApplication(
        Guid id,
        Guid organizationId,
        Guid applicationId,
        bool isActive,
        DateTime enabledAt,
        Guid enabledBy,
        DateTime? expiresAt,
        string? subscriptionTier,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        OrganizationId = organizationId;
        ApplicationId = applicationId;
        IsActive = isActive;
        EnabledAt = enabledAt;
        EnabledBy = enabledBy;
        ExpiresAt = expiresAt;
        SubscriptionTier = subscriptionTier;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates a new organization application subscription.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="applicationId">The application ID</param>
    /// <param name="enabledBy">Who enabled the app</param>
    /// <param name="subscriptionTier">Optional subscription tier</param>
    /// <param name="expiresAt">Optional expiration date</param>
    /// <returns>New OrganizationApplication instance</returns>
    public static OrganizationApplication Create(
        Guid organizationId,
        Guid applicationId,
        Guid enabledBy,
        string? subscriptionTier = null,
        DateTime? expiresAt = null)
    {
        var subscription = new OrganizationApplication
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            IsActive = true,
            EnabledAt = DateTime.UtcNow,
            EnabledBy = enabledBy,
            ExpiresAt = expiresAt,
            SubscriptionTier = subscriptionTier?.Trim()
        };
        subscription.SetCreated(enabledBy);
        return subscription;
    }

    /// <summary>
    /// Checks if the subscription is valid (active and not expired).
    /// </summary>
    public bool IsValid()
    {
        return IsActive && !IsExpired();
    }

    /// <summary>
    /// Checks if the subscription has expired.
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    /// <summary>
    /// Updates the subscription tier.
    /// </summary>
    public void UpdateTier(string? tier, Guid modifiedBy)
    {
        SubscriptionTier = tier?.Trim();
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Extends the subscription expiration.
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
    /// Deactivates the subscription.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Reactivates the subscription.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Reactivates the subscription with new settings.
    /// </summary>
    /// <param name="enabledBy">User who reactivated</param>
    /// <param name="subscriptionTier">New subscription tier</param>
    /// <param name="expiresAt">New expiration date</param>
    public void Reactivate(Guid enabledBy, string? subscriptionTier, DateTime? expiresAt)
    {
        IsActive = true;
        EnabledAt = DateTime.UtcNow;
        EnabledBy = enabledBy;
        SubscriptionTier = subscriptionTier?.Trim();
        ExpiresAt = expiresAt;
        SetModified(enabledBy);
    }
}
