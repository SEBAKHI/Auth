using System.Text.RegularExpressions;

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
