using System.Text.Json;
using System.Text.RegularExpressions;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The registration throttle is one limit spread across two processes and four
/// files, and every one of the ways it can be wired wrong leaves a system that
/// starts, serves traffic, passes its other tests, and quietly enforces the old
/// number.
///
/// <para>
/// Separating <c>POST /auth/register</c> from the shared "login" policy is only
/// half a change. The gateway sits in front and applies its own limiter first,
/// so the effective limit is the LOWER of the two — which means raising the API
/// side alone produces no observable difference at all, and the 429 a client
/// gets back is indistinguishable from the one it got before. That is the
/// failure these tests exist for: not a crash, an absence.
/// </para>
///
/// <para>
/// These read source and configuration text rather than exercising a pipeline,
/// for the same reason <see cref="ThrottlingIdentityGuardTests"/> does: what is
/// being asserted is an arrangement across files, and each file is defensible on
/// its own.
/// </para>
/// </summary>
public class RegistrationThrottleGuardTests
{
    private const string AuthCatchAllPath = "/api/v{version:int}/auth/{**catch-all}";
    private const string RegisterPath = "/api/v{version:int}/auth/register";

    #region The endpoint no longer shares the login bucket

    [Fact]
    public void RegisterEndpoint_OptsIntoItsOwnPolicy_NotTheSharedLoginOne()
    {
        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "AuthController.cs");

        var attributes = AttributeBlockAfter(source, "[HttpPost(\"register\")]");

        attributes.Should().Contain("[EnableRateLimiting(\"register\")]",
            "registration demand is an event while sign-in demand is a habit; sharing one bucket means "
            + "the only way to serve a launch is to widen the limit that also holds sign-in, token "
            + "exchange, account recovery and the deletion challenges");

        attributes.Should().NotContain("[EnableRateLimiting(\"login\")]",
            "an endpoint carrying both attributes takes whichever the framework resolves first, and "
            + "which one that is nobody should have to know");
    }

    /// <summary>
    /// The count is not the point — the enumeration is. Moving another endpoint
    /// onto the registration policy is a decision about what a registration
    /// allowance is for, and it should cost an edit here rather than pass
    /// unnoticed.
    /// </summary>
    [Fact]
    public void OnlyRegistration_UsesTheRegistrationPolicy()
    {
        var root = SolutionDirectory();

        var users = Directory
            .GetFiles(Path.Combine(root, "Auth_API", "Modules"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "EnableRateLimiting\\(\"register\"\\)")
                .Select(_ => Path.GetFileName(file)))
            .ToList();

        users.Should().BeEquivalentTo(["AuthController.cs"]);
    }

    #endregion

    #region The edge half exists, and runs first

    [Fact]
    public void GatewayForwardsRegistration_OnARouteOfItsOwn()
    {
        var routes = GatewayRoutes();

        var register = routes.SingleOrDefault(route => route.Path == RegisterPath);

        register.Should().NotBeNull(
            "without a route of its own, /auth/register falls into auth-route's catch-all and is "
            + "throttled at the sign-in limit — so the API-side limit raised beside it is invisible, "
            + "and the change looks shipped while the number never moves");

        register!.Policy.Should().Be("register");
        register.Policy.Should().NotBe(
            routes.Single(route => route.Path == AuthCatchAllPath).Policy,
            "sharing the auth policy is exactly the state the separate route removes");
    }

    [Fact]
    public void RegistrationRoute_OutranksTheAuthCatchAll()
    {
        var routes = GatewayRoutes();

        var register = routes.Single(route => route.Path == RegisterPath);
        var catchAll = routes.Single(route => route.Path == AuthCatchAllPath);

        // A literal segment already beats {**catch-all} on route precedence, so
        // this is belt and braces — deliberately. The alternative is a throttle
        // whose correctness rests on the next person to touch these routes
        // remembering a YARP precedence rule.
        register.Order.Should().BeLessThan(catchAll.Order,
            "the registration route must win the match against the catch-all it sits inside");
    }

    [Fact]
    public void EveryRouteLimiterPolicy_IsRegisteredInTheGatewayProcess()
    {
        var program = ReadSource("API_Gateway", "Program.cs");

        var registered = Regex.Matches(program, "options\\.AddPolicy\\(\"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        registered.Should().NotBeEmpty("the gateway registers its named policies in Program.cs");

        var unregistered = GatewayRoutes()
            .Where(route => route.Policy is not null && !registered.Contains(route.Policy))
            .Select(route => $"{route.Name} asks for \"{route.Policy}\"")
            .ToList();

        unregistered.Should().BeEmpty(
            "YARP fails the request at runtime when a route names a limiter policy that was never "
            + "registered, and only for requests that reach that route");
    }

    #endregion

    #region Both halves stay live-editable

    [Theory]
    [InlineData("Auth_API", "settingsVersion()")]
    [InlineData("API_Gateway", "SettingsVersion(context)")]
    public void EveryNamedPolicy_StampsTheSettingsVersionIntoItsPartitionKey(string project, string stamp)
    {
        var program = ReadSource(project, "Program.cs");

        var unstamped = new List<string>();
        foreach (System.Text.RegularExpressions.Match policy in
                 Regex.Matches(program, "options\\.AddPolicy\\(\"(?<name>[^\"]+)\""))
        {
            // The partition key is built in the few lines directly after the
            // registration; 600 characters covers the longest of them and stops
            // well short of the next.
            var body = program[policy.Index..Math.Min(program.Length, policy.Index + 600)];

            if (!body.Contains(stamp, StringComparison.Ordinal))
            {
                unstamped.Add(policy.Groups["name"].Value);
            }
        }

        unstamped.Should().BeEmpty(
            "a partition caches its limiter on first hit, so a key without the settings-version stamp "
            + "keeps serving the old limit until every open window idles out. The console reports the "
            + "save succeeded and the limit does not move — which is the worst moment to discover it, "
            + "because the reason anyone edits these fields is that traffic is already arriving");
    }

    /// <summary>
    /// The single highest-cost way this change can go wrong, and it needs no
    /// mistake to trigger — only an ordinary rolling upgrade.
    /// <para>
    /// The gateway reads the API's settings into an optional wire record, so a
    /// field an older API does not send arrives as <c>0</c>. <c>IsUsable</c> is
    /// what stops a partially-populated payload from being applied, because a
    /// <c>PermitLimit</c> of 0 does not slow a route down — it closes it, while
    /// the log cheerfully reports the settings were applied. A field added to
    /// <c>GatewayRateLimits</c> but not to that check is therefore a total outage
    /// of whatever route it governs, for as long as the gateway runs ahead of the
    /// API. Registration is exactly such a field.
    /// </para>
    /// <para>
    /// Asserted against source text because the gateway is a separate process
    /// this test project holds no reference to — the same reason
    /// <see cref="ThrottlingIdentityGuardTests"/> reads its Program.cs.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryGatewayLimitField_IsCheckedBeforeAFetchedPayloadIsApplied()
    {
        var record = ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettings.cs");
        var poller = ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettingsPoller.cs");

        var fields = Between(record, "public sealed record GatewayRateLimits(", ");")
            .Split(',')
            .Select(parameter => parameter.Trim().Split(' ').Last().Trim())
            .Where(name => name.Length > 0)
            .ToList();

        fields.Should().HaveCountGreaterThan(5, "the record declares the gateway's limiter numbers");

        var guard = Between(poller, "private static bool IsUsable(", ";");

        fields.Where(field => !guard.Contains(field, StringComparison.Ordinal))
            .Should().BeEmpty(
                "an unchecked field reaches the limiter as 0 whenever the API has not been upgraded "
                + "yet, and a PermitLimit of 0 refuses every request to that route");
    }

    #endregion

    #region Helpers

    /// <summary>The text between the first occurrence of two markers.</summary>
    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "'{0}' must exist in the source", start);

        from += start.Length;
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from, "'{0}' must close with '{1}'", start, end);

        return source[from..to];
    }

    private sealed record GatewayRoute(string Name, string Path, string? Policy, int Order);

    /// <summary>
    /// The gateway's routes as (name, path, limiter policy, order). Order is the
    /// YARP default of 0 when the route does not state one.
    /// </summary>
    private static List<GatewayRoute> GatewayRoutes()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "API_Gateway", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => new GatewayRoute(
                route.Name,
                route.Value.GetProperty("Match").GetProperty("Path").GetString() ?? string.Empty,
                route.Value.TryGetProperty("RateLimiterPolicy", out var policy) ? policy.GetString() : null,
                route.Value.TryGetProperty("Order", out var order) ? order.GetInt32() : 0))
            .ToList();
    }

    /// <summary>
    /// The attributes between a route attribute and the action signature that
    /// follows it. 600 characters is longer than any block here and shorter than
    /// the next action's body.
    /// </summary>
    private static string AttributeBlockAfter(string source, string routeAttribute)
    {
        var index = source.IndexOf(routeAttribute, StringComparison.Ordinal);
        index.Should().BeGreaterThan(0, "{0} must exist in the controller", routeAttribute);

        return source[index..Math.Min(source.Length, index + 600)];
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

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }

    #endregion
}
