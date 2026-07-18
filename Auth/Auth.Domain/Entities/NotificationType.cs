using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// A category of notification (password reset, email verification, ...) carrying
/// the admin-visible variable catalog and preview sample data. System types back
/// critical auth flows: their global templates cannot be unpublished or deleted.
/// </summary>
public class NotificationType : AggregateRoot
{
    /// <summary>
    /// Gets the stable machine code ('email-verification', 'password-reset', ...).
    /// Calling code references types by this code, never by ID.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the admin-facing display name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the admin-facing description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets whether this type backs a critical system flow.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// Gets the variable catalog as a JSON array of
    /// { name, description, example, required } objects. This is the contract
    /// between calling code and templates, identical across versions and languages.
    /// </summary>
    public string VariablesJson { get; private set; } = "[]";

    /// <summary>
    /// Gets the sample values used for previews and publish-time validation,
    /// as a JSON object keyed by variable name.
    /// </summary>
    public string SampleDataJson { get; private set; } = "{}";

    public bool IsActive { get; private set; }

    private NotificationType() : base()
    {
    }

    public NotificationType(
        Guid id,
        string code,
        string name,
        string? description,
        bool isSystem,
        string variablesJson,
        string sampleDataJson,
        bool isActive,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsSystem = isSystem;
        VariablesJson = variablesJson;
        SampleDataJson = sampleDataJson;
        IsActive = isActive;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Updates the admin-editable metadata (display fields, the variable catalog,
    /// and preview sample data; the code and system flag are immutable).
    /// </summary>
    public void Update(
        string name,
        string? description,
        string variablesJson,
        string sampleDataJson,
        Guid modifiedBy)
    {
        Name = name.Trim();
        Description = description?.Trim();
        VariablesJson = variablesJson;
        SampleDataJson = sampleDataJson;
        SetModified(modifiedBy);
    }
}
