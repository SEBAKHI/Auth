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
