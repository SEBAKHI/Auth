namespace Auth.Domain.Entities;

/// <summary>
/// Sparse configuration overrides for one appsettings section, keyed by the
/// registry section key (e.g. "Jwt"). Only fields an administrator changed
/// are stored; everything else falls through to the configuration files, so
/// deleting the row is a full reset to file values.
/// </summary>
public class SystemSettingsOverride
{
    /// <summary>
    /// Gets the registry section key this row overrides (primary key).
    /// </summary>
    public string SectionKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the sparse nested-JSON override object, mirroring the section's
    /// appsettings shape. Never contains secret-owned keys: writes are
    /// whitelisted against the settings registry before reaching storage.
    /// </summary>
    public string OverridesJson { get; private set; } = "{}";

    /// <summary>
    /// Gets the human-readable save counter (1 on first save, +1 per update).
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets the last modification timestamp (UTC).
    /// </summary>
    public DateTime ModifiedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who last modified the section.
    /// </summary>
    public Guid? ModifiedBy { get; private set; }

    /// <summary>
    /// Gets the SQL Server rowversion used for optimistic concurrency
    /// (null when the entity has not been persisted yet).
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    private SystemSettingsOverride()
    {
    }

    public SystemSettingsOverride(
        string sectionKey,
        string overridesJson,
        int version,
        DateTime modifiedAt,
        Guid? modifiedBy,
        byte[]? rowVersion)
    {
        SectionKey = sectionKey;
        OverridesJson = overridesJson;
        Version = version;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
        RowVersion = rowVersion;
    }

    /// <summary>
    /// Creates a new, not-yet-persisted override row for a section.
    /// </summary>
    public static SystemSettingsOverride Create(string sectionKey, string overridesJson, Guid modifiedBy)
        => new(sectionKey, overridesJson, 1, DateTime.UtcNow, modifiedBy, rowVersion: null);

    /// <summary>
    /// Applies a new override payload and stamps the modification audit
    /// fields. The persisted Version counter is advanced by the repository
    /// as part of the concurrency-checked update.
    /// </summary>
    public void Update(string overridesJson, Guid modifiedBy)
    {
        OverridesJson = overridesJson;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}
