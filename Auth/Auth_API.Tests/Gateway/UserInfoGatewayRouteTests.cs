using System.Text.Json;

namespace Auth_API.Tests.Gateway;

/// <summary>
/// UserInfo at the edge (X11, R6): its own route on the 'api' policy, carved out of the auth
/// catch-all the way the page-load routes are (SignInPageThrottleGuardTests).
/// </summary>
/// <remarks>
/// It is a read with a token an application already holds, not a sign-in attempt. On the auth
/// catch-all it would share the twenty-a-minute sign-in budget of the address it comes from, and
/// an application's server calls it from one address for all of its users.
/// </remarks>
public class UserInfoGatewayRouteTests
{
    private const string Path = "/api/v{version:int}/auth/userinfo";

    [Fact]
    public void UserInfo_HasItsOwnRouteOnTheApiPolicy_AheadOfTheAuthCatchAll()
    {
        var routes = GatewayRoutes();

        var route = routes.SingleOrDefault(r => r.Name == "userinfo-route");
        route.Should().NotBeNull("the edge must forward {0} on a route of its own", Path);
        route!.Path.Should().Be(Path, "an exact path, so nothing else under /auth/ rides on it");
        route.Cluster.Should().Be("auth-cluster");
        route.Policy.Should().Be("api");
        route.Order.Should().Be(-1);

        var catchAll = routes.Single(r => r.Path == "/api/v{version:int}/auth/{**catch-all}");
        catchAll.Policy.Should().Be("auth", "sign-in stays on the sign-in limit");
        route.Order.Should().BeLessThan(catchAll.Order,
            "a literal segment already outranks a catch-all, but the budget a relying party depends on "
            + "should not rest on that being remembered by whoever edits these routes next");
    }

    [Fact]
    public void UserInfo_IsTheOnlyRouteForItsPath()
    {
        GatewayRoutes().Where(r => r.Path == Path).Should().ContainSingle();
    }

    private sealed record GatewayRoute(string Name, string Path, string? Cluster, string? Policy, int Order);

    private static List<GatewayRoute> GatewayRoutes()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(SolutionDirectory(), "API_Gateway", "appsettings.json")),
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

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
