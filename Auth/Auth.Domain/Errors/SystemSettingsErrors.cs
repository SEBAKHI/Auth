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

    public static Error EmailSendingDisabled => Error.Validation(
        code: "SystemSettings.EmailSendingDisabled",
        description: "Email sending is disabled (Email:Enabled is off), so there is nothing to test.");

    public static Error TestEmailFailed(string detail) => Error.Failure(
        code: "SystemSettings.TestEmailFailed",
        description: $"The test email could not be sent: {detail}",
        metadata: new() { ["args"] = new object[] { detail } });

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error FaviconUrlTooLong = Error.Validation(
        code: "SystemSettings.FaviconUrlTooLong",
        description: "URL must not exceed 500 characters.");

    public static readonly Error LogoUrlDarkTooLong = Error.Validation(
        code: "SystemSettings.LogoUrlDarkTooLong",
        description: "URL must not exceed 500 characters.");

    public static readonly Error LogoUrlTooLong = Error.Validation(
        code: "SystemSettings.LogoUrlTooLong",
        description: "URL must not exceed 500 characters.");

    public static readonly Error PlatformNameRequired = Error.Validation(
        code: "SystemSettings.PlatformNameRequired",
        description: "Name is required.");

    public static readonly Error PlatformNameTooLong = Error.Validation(
        code: "SystemSettings.PlatformNameTooLong",
        description: "Name must not exceed 200 characters.");

    public static readonly Error SectionKeyRequired = Error.Validation(
        code: "SystemSettings.SectionKeyRequired",
        description: "Section key is required.");

    public static readonly Error SectionKeyTooLong = Error.Validation(
        code: "SystemSettings.SectionKeyTooLong",
        description: "Section key must not exceed 64 characters.");
}
