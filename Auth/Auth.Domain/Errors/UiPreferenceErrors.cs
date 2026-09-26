using Auth.Domain.Entities;
using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors for per-user client display preferences.
/// </summary>
public static class UiPreferenceErrors
{
    public static Error InvalidKey => Error.Validation(
        code: "UiPreference.InvalidKey",
        description: $"A preference key must start with '{UserUiPreference.TableKeyPrefix}' " +
                     "and contain only lowercase letters, digits and hyphens.");

    public static Error ValueTooLarge => Error.Validation(
        code: "UiPreference.ValueTooLarge",
        description: $"A preference value may be at most {UserUiPreference.MaxValueLength} characters.",
        metadata: new() { ["args"] = new object[] { UserUiPreference.MaxValueLength } });

    public static Error ValueNotJson => Error.Validation(
        code: "UiPreference.ValueNotJson",
        description: "A preference value must be a JSON document.");

    public static Error TooManyKeys => Error.Conflict(
        code: "UiPreference.TooManyKeys",
        description: $"A user may hold at most {UserUiPreference.MaxKeysPerUser} preferences.",
        metadata: new() { ["args"] = new object[] { UserUiPreference.MaxKeysPerUser } });

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error KeyInvalidFormat = Error.Validation(
        code: "UiPreference.KeyInvalidFormat",
        description: "Preference key format is invalid.");

    public static readonly Error KeyRequired = Error.Validation(
        code: "UiPreference.KeyRequired",
        description: "Preference key is required.");

    public static readonly Error KeyTooLong = Error.Validation(
        code: "UiPreference.KeyTooLong",
        description: "Preference key is too long.");

    public static readonly Error ValueRequired = Error.Validation(
        code: "UiPreference.ValueRequired",
        description: "Preference value is required.");

    public static readonly Error ValueTooLong = Error.Validation(
        code: "UiPreference.ValueTooLong",
        description: "Preference value is too long.");
}
