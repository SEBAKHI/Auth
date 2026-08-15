using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Auth.Application.Features.SystemSettings.Common;

/// <summary>
/// Shared projection from registry metadata + override row + live
/// configuration into the section DTO all three handlers return.
/// </summary>
internal static class SystemSettingsProjector
{
    public static SystemSettingsSectionDto BuildSection(
        SettingSectionDefinition definition,
        SystemSettingsOverride? row,
        IConfiguration configuration,
        IStartupValuesSnapshot snapshot,
        string? modifiedByName)
    {
        var overrides = ParseOverrides(row?.OverridesJson);

        return new SystemSettingsSectionDto
        {
            Key = definition.Key,
            Group = definition.Group,
            Editable = definition.Editable,
            Version = row?.Version ?? 0,
            RowVersion = row?.RowVersion is { } rv ? Convert.ToBase64String(rv) : null,
            ModifiedAt = row?.ModifiedAt,
            ModifiedBy = row?.ModifiedBy,
            ModifiedByName = modifiedByName,
            Fields = definition.Fields
                .Select(field => BuildField(definition, field, overrides, configuration, snapshot))
                .ToList()
        };
    }

    private static SystemSettingsFieldDto BuildField(
        SettingSectionDefinition definition,
        SettingFieldDefinition field,
        IReadOnlyDictionary<string, JsonElement> overrides,
        IConfiguration configuration,
        IStartupValuesSnapshot snapshot)
    {
        var kindName = KindName(field.Kind);

        if (field.Sensitive)
        {
            // Secret material: no values ever cross this API, only the fact
            // that the field exists and where it is managed.
            return new SystemSettingsFieldDto
            {
                Path = field.Path,
                Kind = kindName,
                Source = "secrets",
                Sensitive = true,
                ReadOnly = true
            };
        }

        var fullKey = definition.FullKey(field);
        var overrideValue = overrides.TryGetValue(field.Path, out var overrideElement)
            ? JsonToTyped(overrideElement, field.Kind)
            : null;
        var baselineCanonical = snapshot.Baseline(fullKey);
        var isPendingRestart = field.RestartRequired && !string.Equals(
            SettingValueReader.ReadCanonical(configuration, field.Kind, fullKey),
            snapshot.AtStartup(fullKey),
            StringComparison.Ordinal);

        return new SystemSettingsFieldDto
        {
            Path = field.Path,
            Kind = kindName,
            // Coalesce onto the registry default: IConfiguration cannot see
            // settings-class defaults, and "unset" here would misreport
            // values that are in fact active at runtime.
            EffectiveValue = SettingValueReader.ReadTyped(configuration, field.Kind, fullKey) ?? field.DefaultValue,
            OverrideValue = overrideValue,
            BaselineValue = CanonicalToTyped(baselineCanonical, field.Kind) ?? field.DefaultValue,
            // Uncoalesced on purpose: the console shows this as "the system
            // default", so a file value must never be able to masquerade as one.
            DefaultValue = field.DefaultValue,
            Source = overrideValue is not null
                ? "database"
                : baselineCanonical is not null ? "file" : "default",
            RestartRequired = field.RestartRequired,
            IsPendingRestart = isPendingRestart,
            ReadOnly = field.ReadOnly,
            Sensitive = false,
            Min = field.Min,
            Max = field.Max,
            AllowedValues = field.AllowedValues?.ToList()
        };
    }

    /// <summary>
    /// Parses a stored override payload into a field-path → value map
    /// (arrays kept whole). Invalid JSON yields an empty map: the provider
    /// applies the same tolerance, so the view matches what actually loads.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> ParseOverrides(string? overridesJson)
    {
        if (string.IsNullOrWhiteSpace(overridesJson))
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(overridesJson);
            return JsonOverrideFlattener.Flatten(document.RootElement.Clone(), expandArrays: false)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string KindName(SettingKind kind) => kind switch
    {
        SettingKind.Bool => "bool",
        SettingKind.Int => "int",
        SettingKind.Enum => "enum",
        SettingKind.StringArray => "stringArray",
        _ => "string"
    };

    private static object? JsonToTyped(JsonElement element, SettingKind kind)
    {
        return kind switch
        {
            SettingKind.Bool when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            SettingKind.Int when element.ValueKind is JsonValueKind.Number && element.TryGetInt64(out var i) => i,
            SettingKind.StringArray when element.ValueKind is JsonValueKind.Array =>
                element.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToArray(),
            _ => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString()
        };
    }

    private static object? CanonicalToTyped(string? canonical, SettingKind kind)
    {
        if (canonical is null)
        {
            return null;
        }

        return kind switch
        {
            SettingKind.Bool => bool.TryParse(canonical, out var b) ? b : canonical,
            SettingKind.Int => long.TryParse(canonical, out var i) ? i : canonical,
            SettingKind.StringArray => canonical.Split(SettingValueReader.ArraySeparator),
            _ => canonical
        };
    }
}
