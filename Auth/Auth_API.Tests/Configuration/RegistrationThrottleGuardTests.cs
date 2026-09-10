using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The registration throttles are two limits spread across two processes and
/// four files, and every one of the ways they can be wired wrong leaves a
/// system that starts, serves traffic, passes its other tests, and quietly
/// enforces the wrong number.
///
/// <para>
/// A verify-first sign-up is three requests. The first, <c>registration/start</c>,
/// is the one that produces a message, so it counts against the same "register"
/// budget as the legacy <c>POST /auth/register</c> — one permit per sign-up,
/// exactly as before. The two that follow (<c>verify</c>, and <c>complete</c>
/// once it exists) send nothing and there are two of them per start, so they
/// count against a budget of their own, "registration-followup", which must
/// hold at least twice the register limit or sign-ups fail at their second step
/// while the register counter still has room.
/// </para>
///
/// <para>
/// The gateway sits in front and applies its own limiter first, so the
/// effective limit is the LOWER of the two — which means raising the API side
/// alone produces no observable difference at all, and the 429 a client gets
/// back is indistinguishable from the one it got before. That is the failure
/// these tests exist for: not a crash, an absence.
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
    private const string StartPath = "/api/v{version:int}/auth/registration/start";
    private const string FollowupPath = "/api/v{version:int}/auth/registration/{**catch-all}";

    private const string RegisterPolicy = "register";
    private const string FollowupPolicy = "registration-followup";

    #region The endpoints, and which budget each spends

    /// <summary>
    /// Route attribute → the policy that action must carry. The start step
    /// spends the sign-up budget because it is the request that sends mail;
    /// the follow-ups spend their own.
    /// </summary>
    public static TheoryData<string, string> ActionPolicies => new()
    {
        { "[HttpPost(\"register\")]", RegisterPolicy },
        { "[HttpPost(\"registration/start\")]", RegisterPolicy },
        { "[HttpPost(\"registration/verify\")]", FollowupPolicy },
    };

    [Theory]
    [MemberData(nameof(ActionPolicies))]
    public void EachRegistrationAction_OptsIntoItsBudget_NotTheSharedLoginOne(string routeAttribute, string policy)
    {
        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "AuthController.cs");

        var attributes = AttributeBlockAfter(source, routeAttribute);

        attributes.Should().Contain($"[EnableRateLimiting(\"{policy}\")]",
            "registration demand is an event while sign-in demand is a habit; sharing one bucket means "
            + "the only way to serve a launch is to widen the limit that also holds sign-in, token "
            + "exchange, account recovery and the deletion challenges");

        attributes.Should().NotContain("[EnableRateLimiting(\"login\")]",
            "an endpoint carrying both attributes takes whichever the framework resolves first, and "
            + "which one that is nobody should have to know");
    }

    /// <summary>
    /// The count is not the point — the enumeration is. Moving another endpoint
    /// onto either registration policy is a decision about what that allowance
    /// is for, and it should cost an edit here rather than pass unnoticed.
    /// </summary>
    [Fact]
    public void TheTwoRegistrationBudgets_AreSpentByExactlyTheseActions()
    {
        var users = ControllerActionsByPolicy();

        users[RegisterPolicy].Should().BeEquivalentTo(
            [("AuthController.cs", "Register"), ("AuthController.cs", "StartRegistration")],
            "the register budget is one permit per sign-up: the legacy endpoint, and the start step that replaces it");

        users[FollowupPolicy].Should().BeEquivalentTo(
            [("AuthController.cs", "VerifyRegistration")],
            "the follow-up budget is for the steps that send nothing; the completion step joins it when it lands");
    }

    [Fact]
    public void StartAloneCannotExceedTodaysMessageBudget()
    {
        // The start step is the only one of the three that produces a message,
        // and it spends the SAME register budget at the SAME default as the
        // legacy endpoint. So one client IP can cause exactly as much mail per
        // window as it could yesterday — the number was raised to 200 on a
        // measurement (1633176c) and this commit neither raises nor lowers it.
        var program = ReadSource("Auth_API", "Program.cs");
        var registry = SystemSettingsRegistry.TryGet("RateLimiting")!;

        Regex.Match(program, @"GetValue\(""RateLimiting:RegisterPermitLimit"",\s*(?<value>\d+)\)")
            .Groups["value"].Value.Should().Be("200");
        SystemSettingsRegistry.TryGetField(registry, "RegisterPermitLimit")!.DefaultValue.Should().Be(200);
        ReadSection("Auth_API", "RateLimiting")["RegisterPermitLimit"].Should().Be(200);
        ReadSection("API_Gateway", "RateLimiting")["RegisterPermitLimit"].Should().Be(200);
    }

    [Fact]
    public void TheFollowupBudget_CoversTwoRequestsPerSignUp_InEveryHome()
    {
        // A sign-up needs one check and one completion per start. A follow-up
        // limit below twice the register limit refuses sign-ups at their second
        // step while the register counter still has room — and nothing else
        // would say so: the 429 looks like every other 429.
        var api = SystemSettingsRegistry.TryGet("RateLimiting")!;
        var gateway = SystemSettingsRegistry.TryGet("GatewayRateLimiting")!;

        Convert.ToInt64(SystemSettingsRegistry.TryGetField(api, "RegistrationFollowupPermitLimit")!.DefaultValue)
            .Should().BeGreaterThanOrEqualTo(2 * Convert.ToInt64(SystemSettingsRegistry.TryGetField(api, "RegisterPermitLimit")!.DefaultValue));
        Convert.ToInt64(SystemSettingsRegistry.TryGetField(gateway, "RegistrationFollowupPermitLimit")!.DefaultValue)
            .Should().BeGreaterThanOrEqualTo(2 * Convert.ToInt64(SystemSettingsRegistry.TryGetField(gateway, "RegisterPermitLimit")!.DefaultValue));

        foreach (var (project, section) in new[] { ("Auth_API", "RateLimiting"), ("Auth_API", "GatewayRateLimiting"), ("API_Gateway", "RateLimiting") })
        {
            var values = ReadSection(project, section);
            values["RegistrationFollowupPermitLimit"].Should().BeGreaterThanOrEqualTo(2 * values["RegisterPermitLimit"],
                $"{project}/appsettings.json → {section}");
            values["RegistrationFollowupWindowSeconds"].Should().Be(values["RegisterWindowSeconds"],
                "the two budgets are read against the same window, or the ratio above means nothing");
        }
    }

    #endregion

    #region The edge half exists, and runs first

    public static TheoryData<string, string> RoutePolicies => new()
    {
        { RegisterPath, RegisterPolicy },
        { StartPath, RegisterPolicy },
        { FollowupPath, FollowupPolicy },
    };

    [Theory]
    [MemberData(nameof(RoutePolicies))]
    public void GatewayForwardsEachRegistrationPath_OnARouteOfItsOwn(string path, string policy)
    {
        var routes = GatewayRoutes();

        var route = routes.SingleOrDefault(candidate => candidate.Path == path);

        route.Should().NotBeNull(
            $"without a route of its own, {path} falls into auth-route's catch-all and is "
            + "throttled at the sign-in limit — so the API-side limit raised beside it is invisible, "
            + "and the change looks shipped while the number never moves");

        route!.Policy.Should().Be(policy);
        route.Policy.Should().NotBe(
            routes.Single(candidate => candidate.Path == AuthCatchAllPath).Policy,
            "sharing the auth policy is exactly the state the separate route removes");
    }

    [Fact]
    public void TheRegistrationRoutes_OutrankTheAuthCatchAll_AndTheStartOutranksItsOwn()
    {
        var routes = GatewayRoutes();

        var register = routes.Single(route => route.Path == RegisterPath);
        var start = routes.Single(route => route.Path == StartPath);
        var followup = routes.Single(route => route.Path == FollowupPath);
        var catchAll = routes.Single(route => route.Path == AuthCatchAllPath);

        // A literal segment already beats {**catch-all} on route precedence,
        // and a longer catch-all beats a shorter one — so this is belt and
        // braces, deliberately. The alternative is a pair of throttles whose
        // correctness rests on the next person to touch these routes
        // remembering two YARP precedence rules.
        register.Order.Should().BeLessThan(catchAll.Order);
        followup.Order.Should().BeLessThan(catchAll.Order,
            "the follow-up route must win the match against the auth catch-all it sits inside");
        start.Order.Should().BeLessThan(followup.Order,
            "the start route sits inside the follow-up catch-all and spends a different budget, so it must win that match too");
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

    /// <summary>
    /// The two policy bodies are copy-paste neighbours in both processes. A
    /// gateway "registration-followup" body that reads limits.RegisterPermitLimit
    /// enforces 200 where the API enforces 400, and the deploy probe (twenty-one
    /// verifies from one address) passes because it never reaches 200 — so the
    /// second step of every sign-up past the hundredth fails while every test
    /// is green. Each body is therefore bound to its own pair, and only its own.
    /// </summary>
    [Theory]
    [InlineData("API_Gateway", RegisterPolicy, "limits.RegisterPermitLimit", "limits.RegisterWindowSeconds", "limits.RegistrationFollowup")]
    [InlineData("API_Gateway", FollowupPolicy, "limits.RegistrationFollowupPermitLimit", "limits.RegistrationFollowupWindowSeconds", "limits.RegisterPermitLimit")]
    [InlineData("Auth_API", RegisterPolicy, "\"RateLimiting:RegisterPermitLimit\"", "\"RateLimiting:RegisterWindowSeconds\"", "RegistrationFollowup")]
    [InlineData("Auth_API", FollowupPolicy, "\"RateLimiting:RegistrationFollowupPermitLimit\"", "\"RateLimiting:RegistrationFollowupWindowSeconds\"", "\"RateLimiting:RegisterPermitLimit\"")]
    public void EachPolicyBody_ReadsItsOwnPair_AndNotTheNeighbours(string project, string policy, string permit, string window, string neighbour)
    {
        var program = ReadSource(project, "Program.cs");

        var body = PolicyBody(program, policy);

        body.Should().Contain(permit, $"the {policy} policy in {project} must read its own permit limit");
        body.Should().Contain(window, $"the {policy} policy in {project} must read its own window");
        body.Should().NotContain(neighbour, $"the {policy} policy in {project} must not read the neighbouring pair");
    }

    [Fact]
    public void BothProcesses_ReadTheFollowupPair_UnderTheKeysTheConsoleWrites()
    {
        // The API reads RateLimiting:*, the gateway seeds from its own
        // RateLimiting:* and is fed GatewayRateLimiting:* by the internal
        // controller. A pair missing from any of the three is a limit the
        // console offers and nothing applies.
        ReadSource("Auth_API", "Program.cs").Should()
            .Contain("GetValue(\"RateLimiting:RegistrationFollowupPermitLimit\"")
            .And.Contain("GetValue(\"RateLimiting:RegistrationFollowupWindowSeconds\"");
        ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettings.cs").Should()
            .Contain("GetValue(\"RateLimiting:RegistrationFollowupPermitLimit\"")
            .And.Contain("GetValue(\"RateLimiting:RegistrationFollowupWindowSeconds\"");
        ReadSource("Auth_API", "Modules", "Internal", "Controllers", "GatewayRuntimeSettingsController.cs").Should()
            .Contain("GetValue(\"GatewayRateLimiting:RegistrationFollowupPermitLimit\"")
            .And.Contain("GetValue(\"GatewayRateLimiting:RegistrationFollowupWindowSeconds\"");
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
    /// API. Both registration pairs are exactly such fields.
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

        var fields = RecordFields(record, "public sealed record GatewayRateLimits(");

        fields.Should().HaveCountGreaterThan(5, "the record declares the gateway's limiter numbers");
        fields.Should().Contain("RegistrationFollowupPermitLimit");

        var guard = Between(poller, "private static bool IsUsable(", ";");

        fields.Where(field => !guard.Contains(field, StringComparison.Ordinal))
            .Should().BeEmpty(
                "an unchecked field reaches the limiter as 0 whenever the API has not been upgraded "
                + "yet, and a PermitLimit of 0 refuses every request to that route");
    }

    [Fact]
    public void TheWireRecordAndTheResponseRecord_DeclareTheSameFields()
    {
        // The controller's response is what the gateway's record deserializes
        // by name. A field present in one and absent from the other is either
        // never sent (and arrives as 0 — see the guard above) or never read.
        var gateway = RecordFields(ReadSource("API_Gateway", "Configuration", "GatewayRuntimeSettings.cs"), "public sealed record GatewayRateLimits(");
        var api = RecordFields(ReadSource("Auth_API", "Modules", "Internal", "Controllers", "GatewayRuntimeSettingsController.cs"), "public sealed record GatewayRateLimitsResponse(");

        api.Should().Equal(gateway, "the two records are one wire shape, field for field and in order");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The parameter names of a positional record, with comment lines dropped
    /// first: a comment inside the parameter list may carry commas, and a
    /// comma-split alone would turn its words into phantom fields.
    /// </summary>
    private static List<string> RecordFields(string source, string recordStart)
    {
        var body = Between(source, recordStart, ");");
        var code = string.Join("\n", body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        return code
            .Split(',')
            .Select(parameter => parameter.Trim().Split(' ').Last().Trim())
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>
    /// One policy registration's body: from its AddPolicy to the next AddPolicy
    /// (or the rejection handler), so a body is never read past its own end.
    /// </summary>
    private static string PolicyBody(string program, string policy)
    {
        var start = program.IndexOf($"options.AddPolicy(\"{policy}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the {0} policy must be registered", policy);

        var next = program.IndexOf("options.AddPolicy(", start + 1, StringComparison.Ordinal);
        var rejected = program.IndexOf("options.OnRejected", start + 1, StringComparison.Ordinal);
        var end = new[] { next, rejected }.Where(index => index > start).DefaultIfEmpty(program.Length).Min();

        return program[start..end];
    }

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

    /// <summary>
    /// Every controller action that opts into a policy, keyed by policy name:
    /// the attribute is matched to the next action signature after it.
    /// </summary>
    private static Dictionary<string, List<(string File, string Action)>> ControllerActionsByPolicy()
    {
        var root = SolutionDirectory();
        var byPolicy = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal)
        {
            [RegisterPolicy] = [],
            [FollowupPolicy] = [],
        };

        foreach (var file in Directory.GetFiles(Path.Combine(root, "Auth_API", "Modules"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in
                     Regex.Matches(source, @"EnableRateLimiting\(""(?<policy>[^""]+)""\)"))
            {
                var policy = match.Groups["policy"].Value;
                if (!byPolicy.ContainsKey(policy)) continue;

                var signature = Regex.Match(source[match.Index..], @"public\s+async\s+Task<IActionResult>\s+(?<action>\w+)\(");
                signature.Success.Should().BeTrue($"{Path.GetFileName(file)} has a rate-limit attribute with no action after it");
                byPolicy[policy].Add((Path.GetFileName(file), signature.Groups["action"].Value));
            }
        }

        return byPolicy;
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

    /// <summary>The numeric entries of one appsettings section (underscore keys are comments).</summary>
    private static Dictionary<string, long> ReadSection(string project, string sectionName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), project, "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return document.RootElement.GetProperty(sectionName).EnumerateObject()
            .Where(property => !property.Name.StartsWith('_'))
            .ToDictionary(property => property.Name, property => property.Value.GetInt64(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The attributes between a route attribute and the action signature that
    /// follows it. 900 characters is longer than any block here and shorter than
    /// the next action's body.
    /// </summary>
    private static string AttributeBlockAfter(string source, string routeAttribute)
    {
        var index = source.IndexOf(routeAttribute, StringComparison.Ordinal);
        index.Should().BeGreaterThan(0, "{0} must exist in the controller", routeAttribute);

        var block = source[index..Math.Min(source.Length, index + 900)];
        var signature = block.IndexOf("public async Task<IActionResult>", StringComparison.Ordinal);
        return signature > 0 ? block[..signature] : block;
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
