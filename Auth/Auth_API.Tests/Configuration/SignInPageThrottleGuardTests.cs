using System.Text.Json;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Rendering a sign-in page is not attempting to sign in.
/// </summary>
/// <remarks>
/// Two calls happen when the sign-in or sign-up page loads: the list of enabled
/// external providers, and the nonce the Google button must hold before it
/// initialises. Both were counted by the authentication limiter, at twenty per
/// minute per client address — so opening the sign-up page spent two of the
/// twenty and completing a registration spent a third on the verification step.
/// One shared address could therefore finish about six sign-ups a minute, and no
/// registration limit could change that: the registration allowance had been
/// raised to two hundred on measured evidence, and a browser could never reach it
/// because this bucket ran out first.
///
/// <para>
/// The failure was invisible in exactly the way the registration split's was.
/// Nothing errors, nothing logs, and the limit that looks responsible is not the
/// one refusing. These tests hold the arrangement in place on both sides, because
/// either half alone changes nothing a visitor can see.
/// </para>
/// </remarks>
public class SignInPageThrottleGuardTests
{
    private const string PolicyName = "sign-in-page";

    [Theory]
    [InlineData("external-providers")]
    [InlineData("external-nonce")]
    public void ThePageLoadEndpoints_OptIntoTheirOwnPolicy(string route)
    {
        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "AuthController.cs");

        var attributes = AttributeBlockAfter(source, $"(\"{route}\")");

        attributes.Should().Contain($"[EnableRateLimiting(\"{PolicyName}\")]",
            "{0} is spent by rendering a page, and a bucket sized to slow down guessing "
            + "has no business holding it", route);
        attributes.Should().NotContain("[EnableRateLimiting(\"login\")]",
            "{0} sharing the sign-in allowance is the defect these tests exist for", route);
    }

    [Fact]
    public void ThePolicy_IsRegisteredAndConfigurable()
    {
        var program = ReadSource("Auth_API", "Program.cs");

        program.Should().Contain($"options.AddPolicy(\"{PolicyName}\"",
            "a policy name that was never registered throws only when its endpoint is first hit");
        program.Should().Contain("RateLimiting:SignInPagePermitLimit",
            "a limit that cannot be raised from the console cannot be raised during an event");
    }

    /// <summary>
    /// The edge half. Every request meets the gateway first, so the API's policy
    /// alone would change nothing a visitor could observe — the same reason the
    /// registration split needed both halves.
    /// </summary>
    [Theory]
    [InlineData("external-providers-route", "/api/v{version:int}/auth/external-providers")]
    [InlineData("external-nonce-route", "/api/v{version:int}/auth/external-nonce")]
    // S20 PR B: the sign-in page fetches its passkey options on every load.
    [InlineData("passkey-assertion-options-route", "/api/v{version:int}/auth/passkeys/assertion/options")]
    public void TheGateway_CarvesEachPageLoadOutOfTheAuthCatchAll(string routeName, string path)
    {
        var routes = GatewayRoutes();

        var route = routes.SingleOrDefault(r => r.Name == routeName);
        route.Should().NotBeNull("the edge must forward {0} on a route of its own", path);
        route!.Path.Should().Be(path);
        route.Policy.Should().NotBe("auth",
            "leaving it on the catch-all is the arrangement being corrected");

        var catchAll = routes.Single(r => r.Path == "/api/v{version:int}/auth/{**catch-all}");
        route.Order.Should().BeLessThan(catchAll.Order,
            "a literal segment already outranks a catch-all, but the ordering a launch depends on "
            + "should not rest on that being remembered by whoever edits these routes next");
    }

    /// <summary>
    /// S20 PR B: the passkey sign-in and its recovery are sign-in attempts. Only the options the
    /// page fetches on load, and the page's lookup after the address (OI-280), are carved out,
    /// each by its exact path; the two calls that check a passkey stay on the auth catch-all, at
    /// the sign-in limit, like the password sign-in.
    /// </summary>
    [Fact]
    public void ThePasskeySignInAndItsRecovery_StayOnTheSignInLimit()
    {
        var routes = GatewayRoutes();

        routes.Where(route => route.Path.Contains("/auth/passkeys", StringComparison.OrdinalIgnoreCase))
            .Select(route => route.Path)
            .Should().BeEquivalentTo(
                ["/api/v{version:int}/auth/passkeys/assertion/options", "/api/v{version:int}/auth/passkeys/lookup"],
                "only the options and the lookup are carved out of the catch-all, each by an exact path, "
                + "so nothing else under /auth/passkeys rides on them");

        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "PasskeysController.cs");
        AttributeBlockAfter(source, "(\"assertion/options\")").Should().Contain($"[EnableRateLimiting(\"{PolicyName}\")]");
        AttributeBlockAfter(source, "(\"assertion/verify\")").Should().Contain("[EnableRateLimiting(\"login\")]");
        AttributeBlockAfter(source, "(\"assertion/recover\")").Should().Contain("[EnableRateLimiting(\"login\")]");
    }

    /// <summary>
    /// OI-280 (T-A2): the sign-in page's lookup after the address answers about an account, so it
    /// is neither a sign-in attempt nor page rendering. Its own bucket at both layers, with the
    /// same numbers, on a gateway route of its own that outranks the auth catch-all.
    /// </summary>
    /// <remarks>
    /// Deliberate breaks, verified red: the route's policy set to "auth"; the API action's policy
    /// set to "login".
    /// </remarks>
    [Fact]
    public void ThePasskeyLookup_HasItsOwnBucket_AtBothLayers()
    {
        var routes = GatewayRoutes();
        var route = routes.SingleOrDefault(r => r.Name == "passkey-lookup-route");
        route.Should().NotBeNull("the edge must forward the lookup on a route of its own");
        route!.Path.Should().Be("/api/v{version:int}/auth/passkeys/lookup");
        route.Policy.Should().Be("passkey-lookup",
            "on auth every check would spend a sign-in attempt; on api an answer about an account would be priced as page rendering");
        route.Order.Should().BeLessThan(routes.Single(r => r.Path == "/api/v{version:int}/auth/{**catch-all}").Order);

        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "PasskeysController.cs");
        AttributeBlockAfter(source, "(\"lookup\")").Should().Contain("[EnableRateLimiting(\"passkey-lookup\")]");

        ReadSource("Auth_API", "Program.cs").Should().Contain("options.AddPolicy(\"passkey-lookup\"");
        ReadSource("API_Gateway", "Program.cs").Should().Contain("options.AddPolicy(\"passkey-lookup\"",
            "the edge half: a route naming a policy the gateway never registered fails at its first request");
    }

    /// <summary>
    /// S02 PR D: the Google sign-in's code exchange has its own bucket at the API, and a route of
    /// its own at the edge, off the sign-in limit: the sign-in it precedes (external-login) already
    /// spends one sign-in attempt, and on the catch-all a Google sign-in would spend two. It also
    /// carries the stricter Origin filter, since it builds the callback address from the Origin.
    /// </summary>
    /// <remarks>
    /// Deliberate breaks, each verified red: the action's policy set to "login"; the gateway route
    /// removed; the route's policy set to "auth"; <c>[RequireFirstPartyAppOrigin]</c> removed.
    /// </remarks>
    [Fact]
    public void TheGoogleCodeExchange_HasItsOwnBucket_AndItsOwnRouteAtTheEdge()
    {
        var source = ReadSource("Auth_API", "Modules", "Authentication", "Controllers", "AuthController.cs");
        var attributes = AttributeBlockAfter(source, "(\"external/google/code\")");
        attributes.Should().Contain("[EnableRateLimiting(\"external-code\")]");
        attributes.Should().Contain("[RequireFirstPartyAppOrigin]",
            "the callback address is built from the Origin, so a missing or unlisted one is refused");
        attributes.Should().Contain("[AllowAnonymous]");

        var program = ReadSource("Auth_API", "Program.cs");
        program.Should().Contain("options.AddPolicy(\"external-code\"");
        program.Should().Contain("RateLimiting:ExternalCodePermitLimit");
        // The typed client's bounds: without them it would wait 100 s and buffer any size of answer.
        program.Should().Contain("AddHttpClient<IGoogleCodeExchanger, GoogleCodeExchanger>");
        program.Should().Contain("client.Timeout = GoogleCodeExchanger.Timeout;");
        program.Should().Contain("client.MaxResponseContentBufferSize = GoogleCodeExchanger.MaxResponseBytes;");

        var routes = GatewayRoutes();
        var route = routes.SingleOrDefault(r => r.Name == "external-code-route");
        route.Should().NotBeNull("the edge must forward the exchange on a route of its own");
        route!.Path.Should().Be("/api/v{version:int}/auth/external/google/code");
        route.Policy.Should().Be("api", "on auth a Google sign-in would spend two of the twenty sign-in attempts");
        route.Order.Should().BeLessThan(routes.Single(r => r.Path == "/api/v{version:int}/auth/{**catch-all}").Order);
    }

    #region Helpers

    private sealed record GatewayRoute(string Name, string Path, string? Policy, int Order);

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

        directory.Should().NotBeNull("the tests must be able to find Auth.sln above their output folder");
        return directory!.FullName;
    }

    #endregion
}
