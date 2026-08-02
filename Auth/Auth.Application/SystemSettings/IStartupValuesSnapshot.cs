using Microsoft.Extensions.Configuration;

namespace Auth.Application.SystemSettings;

/// <summary>
/// Frozen views of the configuration taken while the process was starting,
/// used to attribute value sources and detect pending-restart changes.
/// </summary>
public interface IStartupValuesSnapshot
{
    /// <summary>
    /// Canonical value of a field BEFORE the database layer was added
    /// (configuration files, environment, secrets) — what the field falls
    /// back to when its override is reset. Null when unset there.
    /// </summary>
    string? Baseline(string fullKey);

    /// <summary>
    /// Canonical value the running process actually booted with (database
    /// layer included). A restart-required field whose current value differs
    /// from this is "pending restart".
    /// </summary>
    string? AtStartup(string fullKey);
}

/// <summary>
/// Immutable snapshot implementation captured in Program.cs: baseline right
/// before the database configuration source is added, startup right after.
/// Sensitive (secret-owned) fields are never captured.
/// </summary>
public sealed class StartupValuesSnapshot : IStartupValuesSnapshot
{
    private readonly IReadOnlyDictionary<string, string?> _baseline;
    private readonly IReadOnlyDictionary<string, string?> _atStartup;

    public StartupValuesSnapshot(
        IReadOnlyDictionary<string, string?> baseline,
        IReadOnlyDictionary<string, string?> atStartup)
    {
        _baseline = baseline;
        _atStartup = atStartup;
    }

    /// <inheritdoc />
    public string? Baseline(string fullKey) => _baseline.GetValueOrDefault(fullKey);

    /// <inheritdoc />
    public string? AtStartup(string fullKey) => _atStartup.GetValueOrDefault(fullKey);

    /// <summary>
    /// Captures the canonical value of every non-sensitive registry field
    /// from the given configuration state.
    /// </summary>
    public static Dictionary<string, string?> CaptureValues(IConfiguration configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in SystemSettingsRegistry.Sections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Sensitive)
                {
                    continue;
                }

                var fullKey = section.FullKey(field);
                values[fullKey] = SettingValueReader.ReadCanonical(configuration, field.Kind, fullKey);
            }
        }

        return values;
    }
}
