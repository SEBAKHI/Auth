using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an external authentication provider (Google, Apple, Facebook, etc.)
/// stored in the database for UI rendering and runtime management.
/// </summary>
public class ExternalAuthProvider : EntityBase
{
    /// <summary>
    /// Gets the unique provider code (e.g., "google", "apple").
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name (e.g., "Google", "Apple").
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the URL to the provider's icon/logo for UI rendering.
    /// </summary>
    public string? IconUrl { get; private set; }

    /// <summary>
    /// Gets whether this provider is currently enabled.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Gets the display order for UI rendering.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when this record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    private ExternalAuthProvider() : base()
    {
    }

    /// <summary>
    /// Constructor for Dapper mapping.
    /// </summary>
    public ExternalAuthProvider(
        Guid id,
        string code,
        string name,
        string? iconUrl,
        bool isEnabled,
        int displayOrder,
        DateTime createdAt,
        DateTime? modifiedAt) : base(id)
    {
        Code = code;
        Name = name;
        IconUrl = iconUrl;
        IsEnabled = isEnabled;
        DisplayOrder = displayOrder;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
    }
}
