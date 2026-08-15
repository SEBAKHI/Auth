namespace Auth.Application.DTOs;

/// <summary>
/// Full system-settings view for the console: every registry section with
/// per-field effective/override/baseline values and apply semantics.
/// </summary>
public record SystemSettingsDto
{
    /// <summary>
    /// True when any restart-required field differs from the value the
    /// running process booted with (page-top banner).
    /// </summary>
    public bool RestartPending { get; init; }

    /// <summary>
    /// True when the most recent database-overrides load failed: values
    /// shown may be stale file values until the database returns.
    /// </summary>
    public bool DbOverridesUnavailable { get; init; }

    public List<SystemSettingsSectionDto> Sections { get; init; } = [];
}

/// <summary>
/// One settings section (one appsettings top section).
/// </summary>
public record SystemSettingsSectionDto
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Navigation group key (security, access, ...).</summary>
    public string Group { get; init; } = string.Empty;

    public bool Editable { get; init; }

    /// <summary>Save counter of the override row; 0 when nothing is overridden.</summary>
    public int Version { get; init; }

    /// <summary>
    /// Base64 rowversion for optimistic concurrency; null when no override
    /// row exists. Must be echoed back on save.
    /// </summary>
    public string? RowVersion { get; init; }

    public DateTime? ModifiedAt { get; init; }

    public Guid? ModifiedBy { get; init; }

    public string? ModifiedByName { get; init; }

    public List<SystemSettingsFieldDto> Fields { get; init; } = [];
}

/// <summary>
/// One setting field with its machine metadata and current values.
/// </summary>
public record SystemSettingsFieldDto
{
    /// <summary>Path relative to the section (":"-separated for nesting).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>bool | int | string | enum | stringArray.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>What the API currently runs with (null for sensitive fields).</summary>
    public object? EffectiveValue { get; init; }

    /// <summary>The stored database override, if any.</summary>
    public object? OverrideValue { get; init; }

    /// <summary>The configuration-file/environment value overrides fall back to.</summary>
    public object? BaselineValue { get; init; }

    /// <summary>
    /// The value shipped with the system, independent of anything this
    /// deployment configured. Distinct from <see cref="BaselineValue"/>, which
    /// is the file value OR this default when no file value exists: that
    /// coalescing is right for "what would I fall back to", and wrong for
    /// telling an administrator what the original number is.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>database | file | default | secrets.</summary>
    public string Source { get; init; } = "default";

    /// <summary>Change applies only after an API restart.</summary>
    public bool RestartRequired { get; init; }

    /// <summary>A restart-required change was saved but is not live yet.</summary>
    public bool IsPendingRestart { get; init; }

    public bool ReadOnly { get; init; }

    /// <summary>Secret material — managed on the Secrets page, never here.</summary>
    public bool Sensitive { get; init; }

    public long? Min { get; init; }

    public long? Max { get; init; }

    public List<string>? AllowedValues { get; init; }
}
