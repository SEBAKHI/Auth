using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Platform-wide branding settings (single-row aggregate).
/// The console UI reads these to render the platform name and logo on the
/// sidebar, auth screens, and the browser tab.
/// </summary>
public class PlatformSettings : EntityBase
{
    /// <summary>
    /// Fixed identifier of the single settings row; enforced by a CHECK
    /// constraint in the database.
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Platform name used when no settings row has been customized yet.
    /// </summary>
    public const string DefaultPlatformName = "Auth Console";

    /// <summary>
    /// Gets the display name of the platform.
    /// </summary>
    public string PlatformName { get; private set; } = DefaultPlatformName;

    /// <summary>
    /// Gets the uploaded logo image key (relative key composed into a public
    /// URL at the API edge), or null when no logo has been uploaded.
    /// </summary>
    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Gets the last modification timestamp.
    /// </summary>
    public DateTime? ModifiedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who last modified the settings.
    /// </summary>
    public Guid? ModifiedBy { get; private set; }

    private PlatformSettings() : base(SingletonId)
    {
    }

    public PlatformSettings(
        Guid id,
        string platformName,
        string? logoUrl,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        PlatformName = platformName;
        LogoUrl = logoUrl;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>
    /// Creates the default settings used before any customization is stored.
    /// </summary>
    public static PlatformSettings CreateDefault() => new();

    /// <summary>
    /// Applies new branding values and stamps the modification audit fields.
    /// </summary>
    public void Update(string platformName, string? logoUrl, Guid modifiedBy)
    {
        PlatformName = platformName;
        LogoUrl = logoUrl;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}
