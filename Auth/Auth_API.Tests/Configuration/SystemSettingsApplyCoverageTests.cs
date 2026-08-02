using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auth.Application.SystemSettings;
using Auth.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The promise the console makes to an administrator: a value saved here is
/// the value the API runs on — never a silent fall-back to appsettings.json.
/// Two independent halves of that promise are guarded here:
/// <list type="number">
/// <item>EVERY editable registry field, when stored, actually materializes on
/// its exact configuration key (catches blocklist/filter drops).</item>
/// <item>No consumer of a settings type that has hot fields still takes the
/// startup-frozen <c>IOptions&lt;T&gt;</c>, outside a documented list of
/// deliberately startup-bound consumers.</item>
/// </list>
/// </summary>
public class SystemSettingsApplyCoverageTests
{
    #region Every editable field reaches configuration

    [Fact]
    public void EveryEditableField_MaterializesOnItsConfigurationKey()
    {
        var rows = SystemSettingsRegistry.Sections
            .Where(s => s.Editable)
            .Select(s => (s.Key, BuildProbePayload(s)))
            .ToList();

        var data = DbSettingsConfigurationProvider.BuildOverrideData(rows, EmptyLengths);

        var missing = new List<string>();
        foreach (var section in SystemSettingsRegistry.Sections.Where(s => s.Editable))
        {
            foreach (var field in section.Fields.Where(f => f.Editable))
            {
                var fullKey = section.FullKey(field);
                var expectedKey = field.Kind == SettingKind.StringArray ? $"{fullKey}:0" : fullKey;

                if (!data.TryGetValue(expectedKey, out var actual) ||
                    actual != ProbeValueAsConfigurationString(field))
                {
                    missing.Add($"{section.Key} → {expectedKey} (got: {(actual is null ? "<absent>" : actual)})");
                }
            }
        }

        missing.Should().BeEmpty(
            "every field the console offers must reach configuration; a dropped key silently keeps the appsettings value");
    }

    [Fact]
    public void EveryEditableField_IsReadableBackThroughConfiguration()
    {
        // End-to-end through the real configuration stack: file layer first,
        // database layer on top — exactly the Program.cs ordering.
        var fileLayer = SystemSettingsRegistry.Sections
            .Where(s => s.Editable)
            .SelectMany(s => s.Fields.Where(f => f.Editable)
                .Select(f => new KeyValuePair<string, string?>(
                    s.FullKey(f) + (f.Kind == SettingKind.StringArray ? ":0" : string.Empty),
                    "file-layer-value")))
            .ToList();

        var rows = SystemSettingsRegistry.Sections
            .Where(s => s.Editable)
            .Select(s => (s.Key, BuildProbePayload(s)))
            .ToList();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(fileLayer)
            .AddInMemoryCollection(DbSettingsConfigurationProvider.BuildOverrideData(rows, EmptyLengths))
            .Build();

        var notOverridden = new List<string>();
        foreach (var section in SystemSettingsRegistry.Sections.Where(s => s.Editable))
        {
            foreach (var field in section.Fields.Where(f => f.Editable))
            {
                var fullKey = section.FullKey(field);
                var effective = SettingValueReader.ReadCanonical(configuration, field.Kind, fullKey);

                if (effective is null || effective.Contains("file-layer-value", StringComparison.Ordinal))
                {
                    notOverridden.Add($"{section.Key} → {fullKey} still reads '{effective}'");
                }
            }
        }

        notOverridden.Should().BeEmpty("the database layer must win over the file layer for every editable field");
    }

    [Fact]
    public void SensitiveAndReadOnlyFields_AreNeverWrittenToConfiguration()
    {
        // The mirror image: values the console must NOT own can never be
        // injected by a hand-crafted (or legacy) stored row.
        var rows = SystemSettingsRegistry.Sections
            .Select(s =>
            {
                var payload = new JsonObject();
                foreach (var field in s.Fields.Where(f => !f.Editable))
                {
                    SetNested(payload, field.Path, JsonValue.Create("injected"));
                }

                return (s.Key, payload.ToJsonString());
            })
            .ToList();

        var data = DbSettingsConfigurationProvider.BuildOverrideData(rows, EmptyLengths);

        data.Should().BeEmpty("secret-owned, read-only and non-editable-section keys must never be loaded from storage");
    }

    #endregion

    #region Hot fields are backed by live-reading consumers

    /// <summary>
    /// Settings types that expose at least one field the console promises to
    /// apply without a restart.
    /// </summary>
    private static readonly string[] TypesWithHotFields =
    [
        "JwtSettings", "PasswordSettings", "SessionSettings", "GatewaySettings",
        "EmailSettings", "NotificationSettings", "AccountDeletionSettings",
        "ImageStorageSettings", "IdentityProviderSettings", "ExternalAuthSettings"
    ];

    /// <summary>
    /// Consumers that deliberately hold the startup snapshot, each because the
    /// value it reads is itself restart-bound (key material, hashing
    /// parameters, one-shot startup work, or the composition root).
    /// </summary>
    private static readonly HashSet<string> StartupBoundConsumers = new(StringComparer.OrdinalIgnoreCase)
    {
        "JwtTokenService.cs",            // signing key + the validation-matched issuer/audience
        "WebhookKeyHasher.cs",           // key material
        "RefreshTokenKeyService.cs",     // key material
        "Argon2PasswordHasher.cs",       // Argon2 parameters (restart-required by the registry)
        "IdentifierHasher.cs",           // key material
        "EncryptionMigrationService.cs", // one-shot startup migration
        "Program.cs"                     // composition root: startup-only reads
    };

    [Fact]
    public void NoHotSettingsType_IsStillConsumedThroughStartupFrozenIOptions()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);
            if (StartupBoundConsumers.Contains(name))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var settingsType in TypesWithHotFields)
            {
                if (text.Contains($"IOptions<{settingsType}>", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}: IOptions<{settingsType}>");
                }
            }
        }

        offenders.Should().BeEmpty(
            "IOptions<T> freezes the startup value, so a console change would silently do nothing; " +
            "use IOptionsSnapshot (scoped) or IOptionsMonitor (singleton/worker), or add the file to " +
            "StartupBoundConsumers with the reason it must stay frozen");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var solutionDirectory = SolutionDirectory();
        string[] projects = ["Auth.Application", "Auth.Infrastructure", "Auth_API"];

        return projects
            .Select(project => Path.Combine(solutionDirectory, project))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }

    #endregion

    #region Probe helpers

    private static readonly Dictionary<string, int> EmptyLengths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A stored payload setting every editable field of the section to a probe
    /// value that is valid for its kind and distinguishable from any file value.
    /// </summary>
    private static string BuildProbePayload(SettingSectionDefinition section)
    {
        var payload = new JsonObject();
        foreach (var field in section.Fields.Where(f => f.Editable))
        {
            SetNested(payload, field.Path, ProbeValue(field));
        }

        return payload.ToJsonString();
    }

    private static JsonNode ProbeValue(SettingFieldDefinition field) => field.Kind switch
    {
        SettingKind.Bool => JsonValue.Create(true),
        SettingKind.Int => JsonValue.Create(ProbeNumber(field)),
        SettingKind.Enum => JsonValue.Create(field.AllowedValues![0]),
        SettingKind.StringArray => new JsonArray(JsonValue.Create("probe-value")),
        _ => JsonValue.Create("probe-value")
    };

    private static long ProbeNumber(SettingFieldDefinition field)
    {
        // Stay inside the field's own advertised range so the probe value is
        // one an administrator could really save.
        var min = field.Min ?? 1;
        var max = field.Max ?? long.MaxValue;
        return min == max ? min : min + 1 <= max ? min + 1 : min;
    }

    private static string ProbeValueAsConfigurationString(SettingFieldDefinition field) => field.Kind switch
    {
        SettingKind.Bool => "true",
        SettingKind.Int => ProbeNumber(field).ToString(),
        SettingKind.Enum => field.AllowedValues![0],
        _ => "probe-value"
    };

    private static void SetNested(JsonObject root, string path, JsonNode? value)
    {
        var segments = path.Split(':');
        var cursor = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (cursor[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                cursor[segments[i]] = child;
            }

            cursor = child;
        }

        cursor[segments[^1]] = value;
    }

    #endregion
}
