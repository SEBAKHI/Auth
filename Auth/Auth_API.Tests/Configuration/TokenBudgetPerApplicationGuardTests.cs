using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Application.SystemSettings;
using Auth_API.Common.RateLimiting;
using Auth_API.Modules.Authentication.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The wiring of the token budget per application (OI-178) in the two Program.cs files and the
/// settings, read as text the way <see cref="TokenEndpointThrottleGuardTests"/> reads the token
/// budget's: the gateway is a separate process this project holds no reference to, and the API's
/// pipeline is built inline. What a server meets is proven on running hosts by
/// <see cref="Authentication.TokenBudgetPerApplicationTests"/> and
/// <see cref="Gateway.TokenAssertionAtTheEdgeTests"/>.
/// </summary>
public class TokenBudgetPerApplicationGuardTests
{
    #region The API half

    [Fact]
    public void TheTokenAction_IsClassified_AndRevocationIsNot()
    {
        var token = typeof(AuthController).GetMethod(nameof(AuthController.Token))!;
        token.GetCustomAttribute<ClassifiesTokenClientAttribute>().Should().NotBeNull(
            "without the mark the middleware never reads the assertion, and every server is held to its address's budget");
        token.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be("token");

        typeof(AuthController).GetMethods()
            .Where(method => method.GetCustomAttribute<ClassifiesTokenClientAttribute>() is not null)
            .Select(method => method.Name)
            .Should().Equal([nameof(AuthController.Token)],
                "revocation ignores assertions (OI-171 B6) and stays on its address's budget");
    }

    [Fact]
    public void TheClassification_RunsImmediatelyBeforeTheLimiter()
    {
        var program = ReadSource("Auth_API", "Program.cs");

        CodeOnly(program).Should().MatchRegex(
            @"app\.UseMiddleware<TokenClientClassificationMiddleware>\(\);\s*app\.UseRateLimiter\(\);",
            "a partition is chosen when the limiter runs: the class must be marked by then, and nothing between may move the request");
    }

    [Fact]
    public void TheTokenPolicy_PartitionsByTheRequestsClass_AndReadsBothPairs()
    {
        var body = PolicyBody(ReadSource("Auth_API", "Program.cs"), "token");

        body.Should().Contain("TokenRequestClass.Of(httpContext).Partition(");
        body.Should().Contain("GetValue(\"RateLimiting:TokenPermitLimit\", 300)");
        body.Should().Contain("GetValue(\"RateLimiting:TokenWindowSeconds\", 60)");
        body.Should().Contain("GetValue(\"RateLimiting:TokenApplicationPermitLimit\", 3000)");
        body.Should().Contain("GetValue(\"RateLimiting:TokenApplicationWindowSeconds\", 60)");
        body.Should().Contain("settingsVersion()");
        body.Should().Contain("ClientIpResolver.Resolve(httpContext)");
    }

    [Fact]
    public void TheFailureAllowance_IsSizedByTheTokenEndpointsPerAddressPair()
    {
        var program = ReadSource("Auth_API", "Program.cs");
        var start = program.IndexOf("new TokenAssertionFailureBudget(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the allowance of assertions that do not verify must be registered");
        var registration = program[start..program.IndexOf(");", start, StringComparison.Ordinal)];

        registration.Should().Contain("settingsVersion()");
        registration.Should().Contain("GetValue(\"RateLimiting:TokenPermitLimit\", 300)");
        registration.Should().Contain("GetValue(\"RateLimiting:TokenWindowSeconds\", 60)");
    }

    [Theory]
    [InlineData("RateLimiting", "TokenApplicationPermitLimit", 3000)]
    [InlineData("RateLimiting", "TokenApplicationWindowSeconds", 60)]
    [InlineData("GatewayRateLimiting", "TokenAssertionPermitLimit", 6000)]
    [InlineData("GatewayRateLimiting", "TokenAssertionWindowSeconds", 60)]
    public void TheConsole_OffersEachNewField_WithTheDecidedDefault(string section, string field, long defaultValue)
    {
        // D-178-4: 3,000 a minute per application, a system setting; 6,000 a minute per address
        // at the edge for the requests that carry an assertion.
        var definition = SystemSettingsRegistry.TryGetField(SystemSettingsRegistry.TryGet(section)!, field);

        definition.Should().NotBeNull("a budget that cannot be raised from the console cannot be raised when a server grows");
        Convert.ToInt64(definition!.DefaultValue).Should().Be(defaultValue);
        definition.Editable.Should().BeTrue();
        definition.RestartRequired.Should().BeFalse("the settings-version stamp applies it to fresh partitions");
    }

    #endregion

    #region The gateway half

    [Fact]
    public void TheGatewaysGlobalBucket_DoesNotCountAssertionBearingTokenRequests()
    {
        var program = ReadSource("API_Gateway", "Program.cs");
        var start = program.IndexOf("PartitionedRateLimiter.Create<HttpContext, string>(context =>", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the gateway builds its global bucket");
        var global = program[start..program.IndexOf("options.GlobalLimiter = globalLimiter;", start, StringComparison.Ordinal)];

        CodeOnly(global).Should().MatchRegex(
            @"if \(ClientAssertionPresenceMiddleware\.IsMarked\(context\)\)\s*\{\s*return RateLimitPartition\.GetNoLimiter\(",
            "a server renewing for thousands of people would otherwise meet the global bucket first");
    }

    [Fact]
    public void TheGatewaysTokenPolicy_GivesAssertionBearingRequestsTheirOwnPerAddressCeiling()
    {
        var program = ReadSource("API_Gateway", "Program.cs");
        var start = program.IndexOf("options.AddPolicy(\"token\"", StringComparison.Ordinal);
        var body = CodeOnly(program[start..program.IndexOf("options.AddPolicy(", start + 1, StringComparison.Ordinal)]);

        body.Should().Contain("ClientAssertionPresenceMiddleware.IsMarked(context)");
        body.Should().MatchRegex(@"\$""v\{SettingsVersion\(context\)\}:assertion:\{ClientId\(context\)\}""",
            "per address, and stamped with the settings version, like every other bucket at the edge");
        body.Should().Contain("PermitLimit = limits.TokenAssertionPermitLimit");
        body.Should().Contain("Window = TimeSpan.FromSeconds(limits.TokenAssertionWindowSeconds)");
    }

    [Fact]
    public void TheGatewayMarksTokenRouteOnly_ImmediatelyBeforeTheLimiter()
    {
        CodeOnly(ReadSource("API_Gateway", "Program.cs")).Should().MatchRegex(
            @"app\.UseMiddleware<ClientAssertionPresenceMiddleware>\(\s*\(Func<HttpContext, bool>\)\(context =>\s*context\.GetEndpoint\(\)\?\.Metadata\.GetMetadata<RouteModel>\(\)\?\.Config\.RouteId == ""token-route""\)\);\s*app\.UseRateLimiter\(\);");
    }

    /// <summary>
    /// E6: a gateway pulling from an API older than itself receives no such pair. The poller
    /// must then keep the values it holds, never run the new bucket on a zero that refuses
    /// every request.
    /// </summary>
    [Fact]
    public void ThePoller_RefusesALimitSetWithoutTheAssertionPair_AndTheFileSeedsIt()
    {
        var poller = ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettingsPoller.cs");
        var isUsable = poller[poller.IndexOf("private static bool IsUsable(", StringComparison.Ordinal)..];
        isUsable = isUsable[..isUsable.IndexOf(';')];

        isUsable.Should().Contain("limits.TokenAssertionPermitLimit > 0");
        isUsable.Should().Contain("limits.TokenAssertionWindowSeconds > 0");
        poller.Should().Contain("(\"TokenAssertionPermitLimit\", limits.TokenAssertionPermitLimit > 0)");

        var provider = ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettings.cs");
        provider.Should().Contain("configuration.GetValue(\"RateLimiting:TokenAssertionPermitLimit\", 6000)");
        provider.Should().Contain("configuration.GetValue(\"RateLimiting:TokenAssertionWindowSeconds\", 60)");
    }

    #endregion

    #region Helpers

    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string PolicyBody(string program, string policy)
    {
        var start = program.IndexOf($"options.AddPolicy(\"{policy}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the {0} policy must be registered", policy);
        return program[start..program.IndexOf("options.AddPolicy(", start + 1, StringComparison.Ordinal)];
    }

    private static string ReadSource(params string[] relativeParts)
        => File.ReadAllText(Path.Combine(SolutionDirectory(), Path.Combine(relativeParts)));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find Auth.sln above their output folder");
        return directory!.FullName;
    }

    #endregion
}
