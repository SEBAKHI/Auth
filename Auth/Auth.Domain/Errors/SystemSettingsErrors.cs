using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors for the dynamic system-settings feature.
/// </summary>
public static class SystemSettingsErrors
{
    public static Error SectionNotFound(string sectionKey) => Error.NotFound(
        code: "SystemSettings.SectionNotFound",
        description: $"Unknown settings section: {sectionKey}.",
        metadata: new() { ["args"] = new object[] { sectionKey } });

    public static Error SectionReadOnly(string sectionKey) => Error.Validation(
        code: "SystemSettings.SectionReadOnly",
        description: $"The settings section '{sectionKey}' is read-only and cannot be changed from the console.",
        metadata: new() { ["args"] = new object[] { sectionKey } });

    public static Error UnknownField(string path) => Error.Validation(
        code: "SystemSettings.UnknownField",
        description: $"The field '{path}' is not an editable setting of this section.",
        metadata: new() { ["args"] = new object[] { path } });

    public static Error SecretManagedField(string path) => Error.Validation(
        code: "SystemSettings.SecretManagedField",
        description: $"The field '{path}' holds secret material and is managed in Secret Management, not here.",
        metadata: new() { ["args"] = new object[] { path } });

    public static Error InvalidFieldValue(string path, string detail) => Error.Validation(
        code: "SystemSettings.InvalidFieldValue",
        description: $"The value for '{path}' is invalid: {detail}",
        metadata: new() { ["args"] = new object[] { path, detail } });

    public static Error ConcurrencyConflict => Error.Conflict(
        code: "SystemSettings.ConcurrencyConflict",
        description: "The section was modified by someone else since you loaded it. Reload and reapply your changes.");
}
