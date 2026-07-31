using System.Text.Json;
using Auth.Application.SystemSettings;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.SystemSettings.Common;

/// <summary>
/// Validates a single override value against its field definition, plus the
/// per-section cross-field rules. Every rule here deliberately mirrors (or
/// is stricter than) the corresponding startup fail-fast so a stored
/// override can never brick a restart.
/// </summary>
internal static class SystemSettingsValueValidator
{
    /// <summary>
    /// Validates one field value; appends errors instead of throwing so a
    /// save reports every problem at once.
    /// </summary>
    public static void ValidateValue(
        SettingFieldDefinition field,
        JsonElement value,
        List<Error> errors)
    {
        switch (field.Kind)
        {
            case SettingKind.Bool:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected true or false."));
                }

                break;

            case SettingKind.Int:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected a whole number."));
                    break;
                }

                if (field.Min is { } min && number < min)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"must be at least {min}."));
                }

                if (field.Max is { } max && number > max)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"must be at most {max}."));
                }

                break;

            case SettingKind.Enum:
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (text is null || field.AllowedValues is null ||
                    !field.AllowedValues.Contains(text, StringComparer.Ordinal))
                {
                    var allowed = field.AllowedValues is null ? string.Empty : string.Join(", ", field.AllowedValues);
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"expected one of: {allowed}."));
                }

                break;

            case SettingKind.StringArray:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected an array of strings."));
                    break;
                }

                foreach (var item in value.EnumerateArray())
                {
                    // Blank entries are forbidden by contract: the provider
                    // uses empty strings as array-shrink tombstones, and an
                    // empty ExemptPaths entry would exempt every request.
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "array entries must be non-empty strings."));
                        break;
                    }
                }

                break;

            default:
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected a string."));
                }

                break;
        }
    }

    /// <summary>
    /// Cross-field / semantic rules per section.
    /// </summary>
    public static void ValidateSectionRules(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        List<Error> errors)
    {
        switch (section.Key)
        {
            case "Jwt":
                foreach (var (path, value) in values)
                {
                    if ((path.Equals("Issuer", StringComparison.OrdinalIgnoreCase) ||
                         path.Equals("Audience", StringComparison.OrdinalIgnoreCase)) &&
                        value.ValueKind == JsonValueKind.String &&
                        !Uri.IsWellFormedUriString(value.GetString(), UriKind.Absolute))
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "must be an absolute URL (e.g. https://auth.example.com)."));
                    }
                }

                break;

            case "Gateway":
                foreach (var (path, value) in values)
                {
                    if (path.Equals("ExemptPaths", StringComparison.OrdinalIgnoreCase) &&
                        value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String &&
                                item.GetString() is { } entry &&
                                !string.IsNullOrWhiteSpace(entry) &&
                                !entry.StartsWith('/'))
                            {
                                errors.Add(SystemSettingsErrors.InvalidFieldValue(path, $"'{entry}' must start with '/'."));
                            }
                        }
                    }
                }

                break;
        }
    }
}
