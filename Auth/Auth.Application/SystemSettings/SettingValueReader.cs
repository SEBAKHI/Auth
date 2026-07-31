using Microsoft.Extensions.Configuration;

namespace Auth.Application.SystemSettings;

/// <summary>
/// Reads registry-described values out of a live <see cref="IConfiguration"/>,
/// either typed (for API responses) or canonicalized to a single comparable
/// string (for restart-pending detection and startup snapshots).
/// </summary>
public static class SettingValueReader
{
    /// <summary>
    /// Unit separator — joins array elements into one canonical string
    /// without colliding with realistic setting values.
    /// </summary>
    public const char ArraySeparator = '\u001f';

    /// <summary>
    /// Reads a field as a typed value for the API response: bool/long/string
    /// or string[] (empty entries dropped — they are array-shrink tombstones
    /// written by the database layer, see the provider). Null when unset.
    /// </summary>
    public static object? ReadTyped(IConfiguration configuration, SettingKind kind, string fullKey)
    {
        if (kind == SettingKind.StringArray)
        {
            var children = configuration.GetSection(fullKey).GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .Cast<object>()
                .ToArray();
            return children.Length > 0 ? children : null;
        }

        var raw = configuration[fullKey];
        if (raw is null)
        {
            return null;
        }

        return kind switch
        {
            SettingKind.Bool => bool.TryParse(raw, out var b) ? b : raw,
            SettingKind.Int => long.TryParse(raw, out var i) ? i : raw,
            _ => raw
        };
    }

    /// <summary>
    /// Reads a field as one canonical string so two configuration states can
    /// be compared (null = unset; arrays joined in index order, tombstone
    /// entries dropped).
    /// </summary>
    public static string? ReadCanonical(IConfiguration configuration, SettingKind kind, string fullKey)
    {
        if (kind == SettingKind.StringArray)
        {
            var values = configuration.GetSection(fullKey).GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            return values.Length > 0 ? string.Join(ArraySeparator, values) : null;
        }

        return configuration[fullKey];
    }
}
