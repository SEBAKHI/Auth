using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents an application registered in the authentication system.
/// Applications provide SSO support and scope isolation for roles/permissions.
/// </summary>
public class Application : AuditableEntityBase
{
    /// <summary>
    /// Gets the unique application code (e.g., "AUTH", "CRM", "ERP").
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name of the application.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of the application.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets whether the application is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets whether this is a system application (cannot be deleted).
    /// </summary>
    public bool IsSystemApplication { get; private set; }

    /// <summary>
    /// Gets the optional URL where the application is hosted.
    /// </summary>
    public string? ApplicationUrl { get; private set; }

    /// <summary>
    /// Gets optional metadata as JSON.
    /// </summary>
    public string? Metadata { get; private set; }

    private Application() : base()
    {
    }

    public Application(
        Guid id,
        string code,
        string name,
        string? description,
        bool isActive,
        bool isSystemApplication,
        string? applicationUrl,
        string? metadata) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsActive = isActive;
        IsSystemApplication = isSystemApplication;
        ApplicationUrl = applicationUrl;
        Metadata = metadata;
    }

    public static Application Create(
        string code,
        string name,
        string? description,
        string? applicationUrl,
        Guid createdBy)
    {
        var application = new Application
        {
            Code = code.ToUpperInvariant(),
            Name = name,
            Description = description,
            IsActive = true,
            IsSystemApplication = false,
            ApplicationUrl = applicationUrl
        };
        application.SetCreated(createdBy);
        return application;
    }

    public void Update(
        string name,
        string? description,
        string? applicationUrl,
        string? metadata,
        Guid modifiedBy)
    {
        Name = name;
        Description = description;
        ApplicationUrl = applicationUrl;
        Metadata = metadata;
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
