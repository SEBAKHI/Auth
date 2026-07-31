namespace Auth.Application.SystemSettings;

/// <summary>
/// The value shape of a single setting field, driving validation and the
/// console's control choice.
/// </summary>
public enum SettingKind
{
    Bool,
    Int,
    String,
    Enum,
    StringArray
}

/// <summary>
/// Group keys the console uses to arrange sections in the setup navigation.
/// Machine identifiers only — display names live in the frontend locales.
/// </summary>
public static class SettingGroups
{
    public const string Security = "security";
    public const string Access = "access";
    public const string Communication = "communication";
    public const string Storage = "storage";
    public const string Operations = "operations";
    public const string Infrastructure = "infrastructure";
}

/// <summary>
/// Machine metadata for one setting field. Human-readable labels and hints
/// are deliberately NOT here: they are localized frontend concerns.
/// </summary>
/// <param name="Path">
/// Config path relative to the section root, ':'-separated for nesting
/// (e.g. "BreachedPasswordCheck:Mode").
/// </param>
/// <param name="Kind">Value shape; drives validation and the UI control.</param>
/// <param name="RestartRequired">
/// True when the running process captures this value at startup, so a saved
/// change only takes effect after a restart (surfaced as a badge).
/// </param>
/// <param name="Sensitive">
/// True for secret material owned by Secret Management: never readable or
/// writable through system settings; the UI links to the Secrets page.
/// </param>
/// <param name="ReadOnly">
/// True for values shown for transparency but not editable from the console
/// (e.g. crypto constants, server disk layout).
/// </param>
/// <param name="DefaultValue">
/// The settings-class default the binder falls back to when neither files
/// nor database configure the key. IConfiguration cannot see class defaults,
/// so without this the console would show "unset" for values that are in
/// fact active at runtime.
/// </param>
public sealed record SettingFieldDefinition(
    string Path,
    SettingKind Kind,
    bool RestartRequired = false,
    bool Sensitive = false,
    bool ReadOnly = false,
    long? Min = null,
    long? Max = null,
    IReadOnlyList<string>? AllowedValues = null,
    object? DefaultValue = null)
{
    /// <summary>
    /// Gets whether the field accepts override writes.
    /// </summary>
    public bool Editable => !Sensitive && !ReadOnly;
}

/// <summary>
/// Machine metadata for one settings section (one appsettings top section,
/// one card group in the console).
/// </summary>
/// <param name="Key">Registry key, also the storage SectionKey (e.g. "Jwt").</param>
/// <param name="ConfigRoot">Configuration path root the fields hang under.</param>
/// <param name="Group">One of <see cref="SettingGroups"/>.</param>
/// <param name="Editable">
/// False for bootstrap sections rendered as read-only info cards
/// (DataProtection, SecretManagement, ConnectionStrings).
/// </param>
public sealed record SettingSectionDefinition(
    string Key,
    string ConfigRoot,
    string Group,
    bool Editable,
    IReadOnlyList<SettingFieldDefinition> Fields)
{
    /// <summary>
    /// Gets the absolute configuration key of a field of this section.
    /// </summary>
    public string FullKey(SettingFieldDefinition field) => $"{ConfigRoot}:{field.Path}";

    /// <summary>
    /// Gets the absolute configuration key of a relative field path.
    /// </summary>
    public string FullKey(string fieldPath) => $"{ConfigRoot}:{fieldPath}";
}
