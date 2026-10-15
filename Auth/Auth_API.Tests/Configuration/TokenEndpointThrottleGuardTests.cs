using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.SystemSettings;
using Auth_API.Modules.Authentication.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The OAuth token endpoint and token revocation have a budget of their own (OI-171).
/// </summary>
/// <remarks>
/// Both used to be counted in the sign-in bucket: twenty a minute per client address, at
/// the API ("login") and at the edge ("auth", through the /auth/ catch-all). A server-side
/// web application (a Backend-for-Frontend) or a fleet of machine clients behind one address
/// sends every user's code exchange and every refresh from that one address, so about three
/// hundred people signed in through one server with 15-minute access tokens already spent
/// the whole allowance, and the people beyond that were refused and signed out. Raising the
/// sign-in limit to serve them would have loosened the password throttle for everyone.
///
/// <para>
/// Nothing at these two endpoints can be guessed: authorization codes and refresh tokens are
/// 256-bit values and a client assertion is a signature. So a high per-address budget costs
/// no brute-force resistance there, while the sign-in bucket keeps its twenty.
/// </para>
///
/// <para>
/// Asserted against source and configuration text, the way the other throttle splits are
/// (<see cref="SignInPageThrottleGuardTests"/>, <see cref="RegistrationThrottleGuardTests"/>):
/// the gateway is a separate process this project holds no reference to, and the API's
/// policies are registered inline in its Program.cs.
/// </para>
/// </remarks>
public class TokenEndpointThrottleGuardTests
{
    private const string PolicyName = "token";
    private const string CatchAllPath = "/api/v{version:int}/auth/{**catch-all}";

    #region The API half

    [Theory]
    [InlineData(" Token([FromForm] OAuthTokenRequest request")]
    [InlineData(" RevokeToken([FromForm] RevokeTokenRequest request")]
    public void TheTokenAndRevokeActions_OptIntoTheTokenPolicy_NotTheSignInOne(string signature)
    {
        var attributes = AttributeBlockAbove(
            ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "AuthController.cs"), signature);

        attributes.Should().Contain($"[EnableRateLimiting(\"{PolicyName}\")]",
            "{0} is spent by a server renewing its users' tokens, and nothing at it can be guessed", signature);
        attributes.Should().NotContain("[EnableRateLimiting(\"login\")]",
            "{0} sharing the sign-in allowance is the defect these tests exist for", signature);
    }

    [Fact]
    public void TheTokenPolicy_IsTheOnlyPolicyTheseTwoActionsSpend()
    {
        // Exactly the two actions: a third endpoint on this budget would be spending an
        // allowance sized for renewals, and an endpoint that checks a password must never be.
        var spenders = Directory
            .GetFiles(Path.Combine(SolutionDirectory(), "Auth_API", "Modules"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), $"\\[EnableRateLimiting\\(\"{PolicyName}\"\\)\\]")
                .Select(_ => Path.GetFileName(file)))
            .ToList();

        spenders.Should().Equal(["AuthController.cs", "AuthController.cs"],
            "the token budget belongs to POST auth/token and POST auth/revoke and nothing else");
        ReadSource("Auth_API", "Program.cs").Should().NotContain($"RequireRateLimiting(\"{PolicyName}\")",
            "an endpoint mapped in Program.cs could spend the token budget without an attribute");
    }

    [Fact]
    public void TheApiPolicy_ReadsItsOwnPair_AndIsStampedWithTheSettingsVersion()
    {
        var body = PolicyBody(ReadSource("Auth_API", "Program.cs"), PolicyName);

        body.Should().Contain("GetValue(\"RateLimiting:TokenPermitLimit\", 300)");
        body.Should().Contain("GetValue(\"RateLimiting:TokenWindowSeconds\", 60)");
        body.Should().NotContain("Login", "the token budget must not read the sign-in pair");
        body.Should().Contain("settingsVersion()",
            "a saved limit reaches new partitions only through the settings-version stamp");
        body.Should().Contain("ClientIpResolver.Resolve(httpContext)",
            "per client address, like the sign-in policy, or one caller would spend everyone's budget");
    }

    /// <summary>
    /// The split must not move the sign-in bucket itself (S3 of the task): password sign-in
    /// from one address is still refused at the twenty-first attempt in a minute.
    /// </summary>
    [Fact]
    public void TheSignInPolicy_KeepsItsPairAndItsDefaults()
    {
        var body = PolicyBody(ReadSource("Auth_API", "Program.cs"), "login");

        body.Should().Contain("GetValue(\"RateLimiting:LoginPermitLimit\", 20)");
        body.Should().Contain("GetValue(\"RateLimiting:LoginWindowSeconds\", 60)");
        body.Should().NotContain("Token");

        var section = SystemSettingsRegistry.TryGet("RateLimiting")!;
        Convert.ToInt64(SystemSettingsRegistry.TryGetField(section, "LoginPermitLimit")!.DefaultValue).Should().Be(20);
        Convert.ToInt64(SystemSettingsRegistry.TryGetField(section, "LoginWindowSeconds")!.DefaultValue).Should().Be(60);

        var gateway = SystemSettingsRegistry.TryGet("GatewayRateLimiting")!;
        Convert.ToInt64(SystemSettingsRegistry.TryGetField(gateway, "AuthPermitLimit")!.DefaultValue).Should().Be(20);
        Convert.ToInt64(SystemSettingsRegistry.TryGetField(gateway, "AuthWindowSeconds")!.DefaultValue).Should().Be(60);
    }

    [Theory]
    [InlineData("RateLimiting")]
    [InlineData("GatewayRateLimiting")]
    public void BothSections_OfferThePair_WithTheSizedDefaults(string sectionKey)
    {
        var section = SystemSettingsRegistry.TryGet(sectionKey);
        section.Should().NotBeNull();

        var permit = SystemSettingsRegistry.TryGetField(section!, "TokenPermitLimit");
        var window = SystemSettingsRegistry.TryGetField(section!, "TokenWindowSeconds");

        permit.Should().NotBeNull("a limit that cannot be raised from the console cannot be raised when a relying party grows");
        window.Should().NotBeNull();
        Convert.ToInt64(permit!.DefaultValue).Should().Be(300);
        Convert.ToInt64(window!.DefaultValue).Should().Be(60);
        permit.Editable.Should().BeTrue();
        window.Editable.Should().BeTrue();
    }

    #endregion

    #region The gateway half

    [Fact]
    public void TheGatewayPolicy_ReadsItsOwnPair_AndIsStampedWithTheSettingsVersion()
    {
        var body = PolicyBody(ReadSource("API_Gateway", "Program.cs"), PolicyName);

        body.Should().Contain("limits.TokenPermitLimit");
        body.Should().Contain("limits.TokenWindowSeconds");
        body.Should().NotContain("limits.Auth", "the token budget must not read the sign-in pair");
        body.Should().Contain("SettingsVersion(context)");
        body.Should().Contain("ClientId(context)");
    }

    [Theory]
    [InlineData("token-route", "/api/v{version:int}/auth/token")]
    [InlineData("revoke-route", "/api/v{version:int}/auth/revoke")]
    public void TheGateway_CarvesEachPathOutOfTheAuthCatchAll_OntoTheTokenPolicy(string routeName, string path)
    {
        var routes = GatewayRoutes();

        var route = routes.SingleOrDefault(r => r.Name == routeName);
        route.Should().NotBeNull("the edge must forward {0} on a route of its own", path);
        route!.Path.Should().Be(path, "an exact path, so nothing else under /auth/ rides on it");
        route.Cluster.Should().Be("auth-cluster");
        route.Policy.Should().Be(PolicyName);
        route.Order.Should().Be(-1);
        routes.Where(r => r.Path == path).Should().ContainSingle();

        var catchAll = routes.Single(r => r.Path == CatchAllPath);
        catchAll.Policy.Should().Be("auth", "sign-in stays on the sign-in limit");
        route.Order.Should().BeLessThan(catchAll.Order,
            "a literal segment already outranks a catch-all, but the budget a relying party depends on "
            + "should not rest on that being remembered by whoever edits these routes next");
    }

    /// <summary>
    /// Carving the two routes changes which route serves exactly two paths, and no other
    /// (S1 of the task). Every route the API's controllers expose under /auth/ is resolved
    /// against the gateway's routes twice — with the two new routes and without them — and
    /// the winners compared.
    /// </summary>
    /// <remarks>
    /// The resolution mirrors ASP.NET Core endpoint selection as YARP uses it: the lowest
    /// Order wins, then the more specific template (a literal path over a catch-all, a longer
    /// catch-all prefix over a shorter one).
    /// </remarks>
    [Fact]
    public void CarvingTheTwoRoutes_MovesOnlyTokenAndRevoke()
    {
        var withTokenRoutes = GatewayRoutes();
        var before = withTokenRoutes.Where(r => r.Name is not ("token-route" or "revoke-route")).ToList();
        before.Count.Should().Be(withTokenRoutes.Count - 2);

        var paths = AuthApiPathsUnderAuth();
        paths.Should().HaveCountGreaterThan(30, "AuthController and TwoFactorController expose dozens of /auth/ routes");
        paths.Should().Contain(["/api/v1/auth/token", "/api/v1/auth/revoke", "/api/v1/auth/login"]);

        var moved = paths
            .Select(path => (Path: path, Before: Resolve(before, path)?.Name, After: Resolve(withTokenRoutes, path)?.Name))
            .Where(result => result.Before != result.After)
            .ToList();

        moved.Should().BeEquivalentTo(
        [
            (Path: "/api/v1/auth/token", Before: (string?)"auth-route", After: (string?)"token-route"),
            (Path: "/api/v1/auth/revoke", Before: (string?)"auth-route", After: (string?)"revoke-route"),
        ]);
    }

    [Fact]
    public void TheEdgeSeed_EqualsTheRegistryDefaults_ForTheTokenPair()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "API_Gateway", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var seed = document.RootElement.GetProperty("RateLimiting");
        seed.GetProperty("TokenPermitLimit").GetInt32().Should().Be(300);
        seed.GetProperty("TokenWindowSeconds").GetInt32().Should().Be(60);
    }

    #endregion

    #region Helpers

    private sealed record GatewayRoute(string Name, string Path, string? Cluster, string? Policy, int Order);

    private static GatewayRoute? Resolve(IEnumerable<GatewayRoute> routes, string path) => routes
        .Where(route => Matches(route.Path, path))
        .OrderBy(route => route.Order)
        .ThenBy(route => route.Path.Contains("{**", StringComparison.Ordinal) ? 1 : 0)
        .ThenByDescending(route => route.Path.Length)
        .FirstOrDefault();

    private static bool Matches(string template, string path)
    {
        var concrete = template.Replace("{version:int}", "1", StringComparison.Ordinal);
        var catchAll = concrete.IndexOf("{**", StringComparison.Ordinal);

        return catchAll < 0
            ? string.Equals(concrete, path, StringComparison.OrdinalIgnoreCase)
            : path.StartsWith(concrete[..catchAll], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every action route the API serves under /api/v1/auth/, parameters filled in.</summary>
    private static List<string> AuthApiPathsUnderAuth()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));

        var paths = new List<string>();
        foreach (var controller in controllers)
        {
            var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;

            foreach (var prefix in controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => r.Template))
            {
                foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    foreach (var verb in method.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                    {
                        var template = string.IsNullOrEmpty(verb.Template) ? prefix : $"{prefix}/{verb.Template}";
                        var path = "/" + Regex.Replace(
                            template
                                .Replace("{version:apiVersion}", "1", StringComparison.OrdinalIgnoreCase)
                                .Replace("[controller]", name, StringComparison.OrdinalIgnoreCase),
                            @"\{[^}]+\}", "x").Trim('/');

                        if (path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase))
                        {
                            paths.Add(path.ToLowerInvariant());
                        }
                    }
                }
            }
        }

        return paths.Distinct().ToList();
    }

    private static List<GatewayRoute> GatewayRoutes()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "API_Gateway", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var routes = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => new GatewayRoute(
                route.Name,
                route.Value.GetProperty("Match").GetProperty("Path").GetString() ?? string.Empty,
                route.Value.TryGetProperty("ClusterId", out var cluster) ? cluster.GetString() : null,
                route.Value.TryGetProperty("RateLimiterPolicy", out var policy) ? policy.GetString() : null,
                route.Value.TryGetProperty("Order", out var order) ? order.GetInt32() : 0))
            .ToList();

        routes.Should().NotBeEmpty("the gateway must define ReverseProxy routes");
        return routes;
    }

    /// <summary>
    /// The registration statement of one named policy: from <c>options.AddPolicy</c> to the
    /// end of its <c>QueueLimit</c> line, the last option every fixed-window policy here sets,
    /// so the comment above the NEXT policy is never read as this one's.
    /// </summary>
    private static string PolicyBody(string program, string policy)
    {
        var start = program.IndexOf($"options.AddPolicy(\"{policy}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the {0} policy must be registered", policy);

        var queueLimit = program.IndexOf("QueueLimit = ", start, StringComparison.Ordinal);
        queueLimit.Should().BeGreaterThan(start, "the {0} policy is a fixed window with a queue limit", policy);
        return program[start..program.IndexOf('\n', queueLimit)];
    }

    private static string AttributeBlockAbove(string source, string signature)
    {
        var index = source.IndexOf(signature, StringComparison.Ordinal);
        index.Should().BeGreaterThan(0, "{0} must exist in the controller", signature);

        var block = source[Math.Max(0, index - 800)..index];
        block = block[(block.LastIndexOf("</summary>", StringComparison.Ordinal) + 1)..];

        // Code lines only: a commented-out attribute must not satisfy the check.
        return string.Join('\n', block.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
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
