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
    /// Cross-field / semantic rules per section. Rules that depend on values
    /// NOT in the payload (e.g. Email:Enabled saved earlier) resolve them via
    /// <paramref name="effectiveValue"/>, so the RESULTING configuration is
    /// validated — mirroring the corresponding startup fail-fasts.
    /// </summary>
    public static void ValidateSectionRules(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        List<Error> errors,
        Func<string, string?> effectiveValue)
    {
        switch (section.Key)
        {
            case "Jwt":
                RequireAbsoluteUrl(values, "Issuer", allowEmpty: false, errors);
                RequireAbsoluteUrl(values, "Audience", allowEmpty: false, errors);
                break;

            case "Gateway":
                ForEachArrayEntry(values, "ExemptPaths", errors, (entry, path) =>
                    entry.StartsWith('/') ? null : $"'{entry}' must start with '/'.");
                break;

            case "IdentityProvider":
                RequireAbsoluteUrl(values, "AccountsBaseUrl", allowEmpty: true, errors);
                RequireAbsoluteUrl(values, "PublicBaseUrl", allowEmpty: true, errors);
                break;

            case "Email":
                // Mirror of the Email options ValidateOnStart rule: sending
                // enabled with a relative FrontendBaseUrl would abort the
                // next boot, so the resulting combination is checked here
                // (payload value first, currently effective value otherwise).
                var enabledText = PayloadOrEffective(values, section, "Enabled", effectiveValue);
                var frontendBaseUrl = PayloadOrEffective(values, section, "FrontendBaseUrl", effectiveValue);
                if (bool.TryParse(enabledText, out var emailEnabled) && emailEnabled &&
                    !Uri.IsWellFormedUriString(frontendBaseUrl, UriKind.Absolute))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(
                        "FrontendBaseUrl", "must be an absolute URL while email sending is enabled."));
                }

                break;

            case "Cors":
                foreach (var (path, value) in values)
                {
                    if (!path.Equals("AllowedOrigins", StringComparison.OrdinalIgnoreCase) ||
                        value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    // An empty override would tombstone every file origin
                    // and cut the console off — mirrors the production
                    // startup fail-fast on empty CORS origins.
                    if (value.GetArrayLength() == 0)
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "must contain at least one origin."));
                    }

                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } origin)
                        {
                            continue;
                        }

                        if (origin.Contains('*'))
                        {
                            errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "wildcard origins are not allowed with credentials."));
                        }
                        else if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                                 uri.AbsolutePath != "/" || origin.EndsWith('/') ||
                                 !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                        {
                            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                                path, $"'{origin}' must be a bare http(s) origin without path or trailing slash."));
                        }
                    }
                }

                break;

            case "ImageStorage":
                foreach (var (path, value) in values)
                {
                    if (path.Equals("PublicBaseUrl", StringComparison.OrdinalIgnoreCase) &&
                        value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { } publicBase &&
                        !publicBase.StartsWith('/') &&
                        !Uri.IsWellFormedUriString(publicBase, UriKind.Absolute))
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(
                            path, "must be an absolute URL or a rooted path starting with '/'."));
                    }
                }

                ForEachArrayEntry(values, "AllowedContentTypes", errors, (entry, path) =>
                    entry.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : $"'{entry}' must be an image/* content type.");
                break;
        }
    }

    private static void RequireAbsoluteUrl(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath,
        bool allowEmpty,
        List<Error> errors)
    {
        foreach (var (path, value) in values)
        {
            if (!path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (allowEmpty && string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (!Uri.IsWellFormedUriString(text, UriKind.Absolute))
            {
                errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "must be an absolute URL (e.g. https://auth.example.com)."));
            }
        }
    }

    private static void ForEachArrayEntry(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath,
        List<Error> errors,
        Func<string, string, string?> rule)
    {
        foreach (var (path, value) in values)
        {
            if (!path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    item.GetString() is { } entry &&
                    !string.IsNullOrWhiteSpace(entry) &&
                    rule(entry, path) is { } message)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(path, message));
                }
            }
        }
    }

    /// <summary>
    /// The value the configuration WILL have after this save: the payload's
    /// value when the field is part of it, the live effective value otherwise.
    /// </summary>
    private static string? PayloadOrEffective(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        string fieldPath,
        Func<string, string?> effectiveValue)
    {
        foreach (var (path, value) in values)
        {
            if (path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.GetRawText()
                };
            }
        }

        return effectiveValue(section.FullKey(fieldPath));
    }
}
