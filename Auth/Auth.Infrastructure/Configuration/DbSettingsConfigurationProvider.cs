using System.Text.Json;
using Auth.Application.SystemSettings;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Configuration;

/// <summary>
/// Configuration source layering the SystemSettingsOverrides table over the
/// file/environment/secret layers. Built once in Program.cs; the concrete
/// provider instance is kept so it can be registered in DI as the
/// <see cref="ISystemSettingsReloader"/>.
/// </summary>
public sealed class DbSettingsConfigurationSource : IConfigurationSource
{
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, int> _baselineArrayLengths;

    public DbSettingsConfigurationSource(
        string connectionString,
        IReadOnlyDictionary<string, int> baselineArrayLengths)
    {
        _connectionString = connectionString;
        _baselineArrayLengths = baselineArrayLengths;
    }

    /// <summary>
    /// Gets the provider instance once the configuration system built it.
    /// </summary>
    public DbSettingsConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => Provider ??= new DbSettingsConfigurationProvider(_connectionString, _baselineArrayLengths);
}

/// <summary>
/// Loads per-section override JSON from the database and flattens it into
/// configuration keys. Trust boundaries:
/// <list type="bullet">
/// <item>Only keys the settings registry declares editable are loaded —
/// unknown sections/fields and secret-owned keys are ignored, so the secret
/// layer and this layer hold disjoint key sets by construction.</item>
/// <item>Fail-open: any load failure keeps the previous data (file values on
/// a cold start) instead of taking the process down. The database being
/// down must never stop authentication.</item>
/// <item>Shrunk arrays are padded with empty-string tombstones up to the
/// file-layer length, because .NET configuration merges arrays index-wise
/// and residual file elements would otherwise leak through. Array consumers
/// filter empty entries.</item>
/// </list>
/// </summary>
public sealed class DbSettingsConfigurationProvider : ConfigurationProvider, ISystemSettingsReloader
{
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, int> _baselineArrayLengths;
    private readonly Lock _sync = new();
    private volatile bool _lastLoadFailed;

    public DbSettingsConfigurationProvider(
        string connectionString,
        IReadOnlyDictionary<string, int> baselineArrayLengths)
    {
        _connectionString = connectionString;
        _baselineArrayLengths = baselineArrayLengths;
    }

    /// <inheritdoc />
    public bool LastLoadFailed => _lastLoadFailed;

    /// <summary>
    /// Captures the array lengths of the pre-database configuration state,
    /// needed for shrink tombstones. Must run BEFORE this source is added.
    /// </summary>
    public static Dictionary<string, int> CaptureBaselineArrayLengths(IConfiguration configuration)
    {
        var lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in SystemSettingsRegistry.Sections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Kind != SettingKind.StringArray)
                {
                    continue;
                }

                var fullKey = section.FullKey(field);
                lengths[fullKey] = configuration.GetSection(fullKey).GetChildren().Count();
            }
        }

        return lengths;
    }

    public override void Load() => LoadCore();

    /// <inheritdoc />
    public void Reload()
    {
        if (LoadCore())
        {
            OnReload();
        }
    }

    /// <summary>
    /// Loads overrides; returns true when the flattened data actually
    /// changed. On failure the previous data is kept (last known good).
    /// </summary>
    private bool LoadCore()
    {
        lock (_sync)
        {
            List<(string SectionKey, string OverridesJson)> rows;
            try
            {
                rows = QueryRows();
                _lastLoadFailed = false;
            }
            catch (Exception ex)
            {
                // Mirrors DpapiSecretConfigurationProvider: warn and fall
                // back — no logger exists this early in the host lifecycle.
                _lastLoadFailed = true;
                Console.WriteLine(
                    $"Warning: could not load system-settings overrides from the database ({ex.GetType().Name}: {ex.Message}). " +
                    "Running on configuration-file values until the database is reachable.");
                return false;
            }

            var data = BuildData(rows);
            if (DataEquals(Data, data))
            {
                return false;
            }

            Data = data;
            return true;
        }
    }

    private List<(string, string)> QueryRows()
    {
        var rows = new List<(string, string)>();

        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT [SectionKey], [OverridesJson] FROM [dbo].[SystemSettingsOverrides]";
        command.CommandTimeout = 5;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private Dictionary<string, string?> BuildData(List<(string SectionKey, string OverridesJson)> rows)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sectionKey, overridesJson) in rows)
        {
            var section = SystemSettingsRegistry.TryGet(sectionKey);
            if (section is null || !section.Editable)
            {
                continue;
            }

            IReadOnlyList<KeyValuePair<string, JsonElement>> flattened;
            try
            {
                using var document = JsonDocument.Parse(overridesJson);
                flattened = JsonOverrideFlattener.Flatten(document.RootElement.Clone(), expandArrays: false);
            }
            catch (JsonException)
            {
                Console.WriteLine($"Warning: ignoring invalid override JSON for settings section '{sectionKey}'.");
                continue;
            }

            foreach (var (path, value) in flattened)
            {
                var field = SystemSettingsRegistry.TryGetField(section, path);
                if (field is null || !field.Editable)
                {
                    continue;
                }

                var fullKey = section.FullKey(field);
                if (SecretOwnedKeys.IsSecretOwned(fullKey))
                {
                    continue;
                }

                if (field.Kind == SettingKind.StringArray && value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            data[$"{fullKey}:{index}"] = item.GetString();
                            index++;
                        }
                    }

                    // Tombstones: mask residual file elements past the
                    // override's length.
                    var baselineLength = _baselineArrayLengths.GetValueOrDefault(fullKey);
                    for (; index < baselineLength; index++)
                    {
                        data[$"{fullKey}:{index}"] = string.Empty;
                    }
                }
                else if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
                {
                    data[fullKey] = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => value.GetRawText()
                    };
                }
            }
        }

        return data;
    }

    private static bool DataEquals(IDictionary<string, string?> left, IDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// No-op reloader bound when the database settings layer is disabled via
/// the AUTH_DISABLE_DB_SETTINGS escape hatch.
/// </summary>
public sealed class NullSystemSettingsReloader : ISystemSettingsReloader
{
    public void Reload()
    {
    }

    public bool LastLoadFailed => false;
}
