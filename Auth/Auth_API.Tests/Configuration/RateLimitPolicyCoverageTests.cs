using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Keeps the named rate-limit policies and the endpoints that opt into them in
/// agreement.
///
/// There is no default bucket in this process, deliberately: a shared limiter on
/// an auth surface lets one caller lock everyone out. The cost of that choice is
/// that an endpoint with no <c>[EnableRateLimiting]</c> is simply unlimited, and
/// nothing says so — which is how <c>POST /apikeys/validate</c> came to run a full
/// Argon2id verify per candidate key, at nineteen megabytes each, as many times a
/// second as a caller cared to ask.
///
/// A policy name is a magic string on both sides. Referencing one that was never
/// registered throws only when that endpoint is first hit, so it survives every
/// test that does not exercise it.
/// </summary>
public class RateLimitPolicyCoverageTests
{
    /// <summary>
    /// Endpoints whose work is unbounded per request and must therefore be
    /// throttled: they hash, they hold a connection, or they hand work to
    /// something that does. The pair is (controller file, action name).
    /// </summary>
    public static readonly TheoryData<string, string> MustBeThrottled = new()
    {
        { "ApiKeyManagement/Controllers/ApiKeysController.cs", "ValidateApiKey" },
    };

    [Theory]
    [MemberData(nameof(MustBeThrottled))]
    public void ExpensiveEndpoints_OptIntoAPolicy(string controller, string action)
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Modules", controller));

        var actionIndex = source.IndexOf($" {action}(", StringComparison.Ordinal);
        actionIndex.Should().BeGreaterThan(0, $"{action} must exist in {controller}");

        // The attribute block sits directly above the signature; 800 characters is
        // more than any of them and less than the previous action's body.
        var attributes = source[Math.Max(0, actionIndex - 800)..actionIndex];

        attributes.Should().Contain("[EnableRateLimiting(",
            $"{action} does unbounded work per call, so an unthrottled caller decides how much of it this process does");
    }

    [Fact]
    public void EveryPolicyAnEndpointAsksFor_IsRegistered()
    {
        var root = SolutionDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Auth_API", "Program.cs"));

        var registered = Regex.Matches(program, @"options\.AddPolicy\(""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        registered.Should().NotBeEmpty("Program.cs registers the named policies");

        var requested = Directory
            .GetFiles(Path.Combine(root, "Auth_API", "Modules"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"EnableRateLimiting\(""(?<name>[^""]+)""")
                .Select(match => (File: Path.GetFileName(file), Policy: match.Groups["name"].Value)))
            .ToList();

        var unregistered = requested
            .Where(request => !registered.Contains(request.Policy))
            .Select(request => $"{request.File} asks for \"{request.Policy}\"")
            .Distinct()
            .ToList();

        unregistered.Should().BeEmpty(
            "a policy name that was never registered throws at the first request to that endpoint "
            + "and nowhere else, so it ships looking fine");
    }

    [Fact]
    public void EveryRegisteredPolicy_IsConfigurable()
    {
        var root = SolutionDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Auth_API", "Program.cs"));
        var registry = File.ReadAllText(Path.Combine(
            root, "Auth.Application", "SystemSettings", "SystemSettingsRegistry.cs"));

        // Every limit read from configuration must also be a field the console can
        // edit. A limit that exists only in appsettings is invisible to the person
        // who needs to raise it at three in the morning.
        var keys = Regex.Matches(program, @"GetValue\(""RateLimiting:(?<field>[^""]+)""")
            .Select(match => match.Groups["field"].Value)
            .Distinct()
            .ToList();

        keys.Should().NotBeEmpty("the policies read their limits from configuration");

        var missing = keys
            .Where(field => !registry.Contains($"\"{field}\"", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty("SystemSettingsRegistry must expose every RateLimiting field as an editable setting");
    }

    /// <summary>
    /// Each of these limits is written down three times — as the fallback
    /// <c>GetValue</c> is given in Program.cs, as the file-layer value in
    /// appsettings.json, and as the registry's DefaultValue — and nothing in the
    /// compiler ties the three together.
    ///
    /// <para>
    /// Drift between them is silent and each way of drifting misleads someone
    /// different. The registry's copy is the number the console prints under
    /// "Default", so a stale one tells an administrator the system reverts to a
    /// value it does not. The Program.cs copy is what actually runs whenever a
    /// deployment ships without that key in its appsettings — the ordinary state
    /// of a rolling upgrade that adds a limit — so a stale one is a throttle
    /// nobody chose, enforced in production, agreeing with no file in the repo.
    /// </para>
    ///
    /// <para>
    /// The gateway's half of this has been guarded since its section was written
    /// (GatewayRateLimitingParityTests). This is the same guard for the limits
    /// this process applies to itself.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryLimit_ReadsTheSameNumberFromAllThreeOfItsHomes()
    {
        var root = SolutionDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Auth_API", "Program.cs"));

        var section = SystemSettingsRegistry.TryGet("RateLimiting");
        section.Should().NotBeNull();

        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "Auth_API", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var fileLayer = settings.RootElement.GetProperty("RateLimiting");

        var fallbacks = Regex.Matches(program, @"GetValue\(""RateLimiting:(?<field>[^""]+)"",\s*(?<value>\d+)\)")
            .Select(match => (Field: match.Groups["field"].Value, Value: long.Parse(match.Groups["value"].Value)))
            .ToList();

        fallbacks.Should().NotBeEmpty("the policies pass a literal fallback to every GetValue");

        var disagreements = new List<string>();
        foreach (var (field, codeFallback) in fallbacks)
        {
            if (!fileLayer.TryGetProperty(field, out var fileValue))
            {
                disagreements.Add($"{field}: absent from appsettings.json");
                continue;
            }

            if (fileValue.GetInt64() != codeFallback)
            {
                disagreements.Add($"{field}: Program.cs says {codeFallback}, appsettings.json says {fileValue.GetInt64()}");
            }

            var registryField = SystemSettingsRegistry.TryGetField(section!, field);
            if (registryField is null)
            {
                // Reported by EveryRegisteredPolicy_IsConfigurable with its own reason.
                continue;
            }

            if (Convert.ToInt64(registryField.DefaultValue) != codeFallback)
            {
                disagreements.Add(
                    $"{field}: Program.cs says {codeFallback}, the registry default says {registryField.DefaultValue}");
            }
        }

        disagreements.Should().BeEmpty();
    }

    /// <summary>
    /// The mirror of the rule above: a key sitting in appsettings that no policy
    /// reads is a limit no endpoint applies. Three of them lived here once,
    /// feeding a policy nothing was attached to, while the console offered them
    /// as live controls.
    /// </summary>
    [Fact]
    public void NoLimitSitsInTheFile_WithoutAPolicyThatReadsIt()
    {
        var root = SolutionDirectory();
        var program = File.ReadAllText(Path.Combine(root, "Auth_API", "Program.cs"));

        var read = Regex.Matches(program, @"GetValue\(""RateLimiting:(?<field>[^""]+)""")
            .Select(match => match.Groups["field"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "Auth_API", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        settings.RootElement.GetProperty("RateLimiting").EnumerateObject()
            // Underscore-prefixed keys are this repository's comment convention.
            .Where(property => !property.Name.StartsWith('_'))
            .Select(property => property.Name)
            .Where(name => !read.Contains(name))
            .Should().BeEmpty("a limit no policy reads throttles nothing while looking like a setting");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
