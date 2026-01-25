using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an organization (tenant) in the multi-tenant authentication system.
/// Organizations can subscribe to applications and manage their members.
/// </summary>
public class Organization : AuditableEntityBase
{
    /// <summary>
    /// Gets the unique organization code (slug) used in URLs and references.
    /// Example: "acme-corp"
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name of the organization.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of the organization.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the URL of the organization's logo.
    /// </summary>
    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Gets the organization's website URL.
    /// </summary>
    public string? Website { get; private set; }

    /// <summary>
    /// Gets the primary contact email for the organization.
    /// </summary>
    public string ContactEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ID of the user who owns the organization.
    /// The owner has full control and cannot be removed.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Gets whether the organization is currently active.
    /// Inactive organizations cannot be accessed.
    /// </summary>
    public bool IsActive { get; private set; }

    private Organization() : base()
    {
    }

    public Organization(
        Guid id,
        string code,
        string name,
        string? description,
        string? logoUrl,
        string? website,
        string contactEmail,
        Guid ownerId,
        bool isActive,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        LogoUrl = logoUrl;
        Website = website;
        ContactEmail = contactEmail;
        OwnerId = ownerId;
        IsActive = isActive;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates a new organization.
    /// </summary>
    /// <param name="code">Unique organization code (will be lowercased)</param>
    /// <param name="name">Display name</param>
    /// <param name="contactEmail">Primary contact email</param>
    /// <param name="ownerId">User ID of the owner</param>
    /// <param name="description">Optional description</param>
    /// <param name="logoUrl">Optional logo URL</param>
    /// <param name="website">Optional website URL</param>
    /// <returns>New Organization instance</returns>
    public static Organization Create(
        string code,
        string name,
        string contactEmail,
        Guid ownerId,
        string? description = null,
        string? logoUrl = null,
        string? website = null)
    {
        var organization = new Organization
        {
            Code = code.ToLowerInvariant().Trim(),
            Name = name.Trim(),
            Description = description?.Trim(),
            LogoUrl = logoUrl?.Trim(),
            Website = website?.Trim(),
            ContactEmail = contactEmail.ToLowerInvariant().Trim(),
            OwnerId = ownerId,
            IsActive = true
        };
        organization.SetCreated(ownerId);
        return organization;
    }

    /// <summary>
    /// Updates the organization details.
    /// </summary>
    public void Update(
        string name,
        string? description,
        string? logoUrl,
        string? website,
        string contactEmail,
        Guid modifiedBy)
    {
        Name = name.Trim();
        Description = description?.Trim();
        LogoUrl = logoUrl?.Trim();
        Website = website?.Trim();
        ContactEmail = contactEmail.ToLowerInvariant().Trim();
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Transfers ownership to another user.
    /// </summary>
    /// <param name="newOwnerId">The new owner's user ID</param>
    /// <param name="modifiedBy">User making the change</param>
    public void TransferOwnership(Guid newOwnerId, Guid modifiedBy)
    {
        OwnerId = newOwnerId;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Activates the organization.
    /// </summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Deactivates the organization.
    /// </summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }
}
