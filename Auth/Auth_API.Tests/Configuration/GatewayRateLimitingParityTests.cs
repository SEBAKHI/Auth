using System.Text.Json;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// One number, three homes — and nothing in the compiler connects them.
/// <list type="bullet">
/// <item>the registry default, which the console shows as "the system value";</item>
/// <item><c>Auth_API/appsettings.json → GatewayRateLimiting</c>, the file layer
/// the settings pull serves when no override is stored;</item>
/// <item><c>API_Gateway/appsettings.json → RateLimiting</c>, the seed the edge
/// boots on and falls back to whenever this API is unreachable.</item>
/// </list>
/// Drift between them is invisible at runtime: the console would display one
/// limit while the gateway enforced another, and the disagreement would only
/// surface as an operator being throttled at a number the UI says is not set.
/// </summary>
public class GatewayRateLimitingParityTests
{
    private const string RegistryKey = "GatewayRateLimiting";
    private const string ApiSection = "GatewayRateLimiting";
    private const string GatewaySection = "RateLimiting";

    [Fact]
    public void ApiFileLayer_MatchesTheRegistryDefaults()
    {
        var file = ReadSection("Auth_API", ApiSection);

        foreach (var field in RegistryFields())
        {
            file.Should().ContainKey(field.Path,
                "Auth_API/appsettings.json must carry every field the console offers");

            file[field.Path].Should().Be(
                Convert.ToInt64(field.DefaultValue),
                "{0} disagrees with its registry default", field.Path);
        }
    }

    [Fact]
    public void GatewayFileLayer_MatchesTheApiFileLayer()
    {
        var api = ReadSection("Auth_API", ApiSection);
        var gateway = ReadSection("API_Gateway", GatewaySection);

        gateway.Should().BeEquivalentTo(api,
            "the gateway's own seed is what it runs on until the first successful " +
            "settings pull, and what it falls back to during an API outage");
    }

    [Fact]
    public void NeitherFileCarriesAKeyTheConsoleCannotEdit()
    {
        // A key present in a file but absent from the registry is a limit no
        // administrator can reach — the exact state this whole change removed.
        var known = RegistryFields().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        ReadSection("Auth_API", ApiSection).Keys.Should().OnlyContain(key => known.Contains(key));
        ReadSection("API_Gateway", GatewaySection).Keys.Should().OnlyContain(key => known.Contains(key));
    }

    private static IEnumerable<SettingFieldDefinition> RegistryFields()
    {
        var section = SystemSettingsRegistry.TryGet(RegistryKey);
        section.Should().NotBeNull();
        return section!.Fields;
    }

    /// <summary>
    /// The numeric entries of one appsettings section. Underscore-prefixed keys
    /// are this repository's comment convention and carry no value.
    /// </summary>
    private static Dictionary<string, long> ReadSection(string project, string sectionName)
    {
        var path = Path.Combine(SolutionDirectory(), project, "appsettings.json");
        File.Exists(path).Should().BeTrue("{0} must exist to be compared", path);

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        document.RootElement.TryGetProperty(sectionName, out var section)
            .Should().BeTrue("{0} must declare a '{1}' section", path, sectionName);

        return section.EnumerateObject()
            .Where(property => !property.Name.StartsWith('_'))
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetInt64(),
                StringComparer.OrdinalIgnoreCase);
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
}
