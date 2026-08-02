using System.Text.Json;
using Auth_API.Modules.Media.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Gateway;

/// <summary>
/// Guards against gateway route drift: in production the SPA reaches the Auth API only
/// through the YARP gateway, whose routes are an explicit per-feature allowlist in
/// API_Gateway/appsettings.json. Routes are version-agnostic (v{version:int} matches
/// every numeric API version), but a controller for a NEW feature without a matching
/// gateway route surfaces as a bodiless 404 for that whole feature — images, dashboard
/// and webhook keys have each drifted this way. These tests fail as soon as a controller
/// (or the uploads static path) is not forwarded by any gateway route.
/// </summary>
public class GatewayRouteCoverageTests
{
    /// <summary>
    /// Controllers that must NOT be forwarded, each with the reason. An entry
    /// here is a decision to keep an endpoint off the internet, so it costs a
    /// deliberate edit rather than a silently passing test.
    /// </summary>
    private static readonly Dictionary<string, string> NotForwardedOnPurpose = new(StringComparer.Ordinal)
    {
        // Server-to-server only: the gateway calls it directly to learn the
        // settings it consumes, authenticated by the shared gateway token.
        // Publishing it through the gateway would expose an internal endpoint
        // for no caller that needs it.
        ["GatewayRuntimeSettingsController"] = "internal, called by the gateway process itself"
    };

    [Fact]
    public void EveryControllerRoute_IsCoveredByAGatewayRoute()
    {
        var gatewayPrefixes = GatewayRoutePrefixes();
        var uncovered = new List<string>();

        var controllers = typeof(ImagesController).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && !type.IsAbstract
                && typeof(ControllerBase).IsAssignableFrom(type)
                && !NotForwardedOnPurpose.ContainsKey(type.Name));

        foreach (var controller in controllers)
        {
            foreach (var route in controller.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                         .Cast<RouteAttribute>())
            {
                var path = NormalizeControllerRoute(route.Template, controller.Name);
                if (!gatewayPrefixes.Any(prefix => (path + "/").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    uncovered.Add($"{controller.Name} -> {path}");
                }
            }
        }

        uncovered.Should().BeEmpty(
            "every Auth API controller must be reachable through the gateway; " +
            "add a matching route to Auth/API_Gateway/appsettings.json (ReverseProxy:Routes)");
    }

    [Fact]
    public void ImageStorageRequestPath_IsCoveredByAGatewayRoute()
    {
        using var apiSettings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "appsettings.json")));

        var requestPath = apiSettings.RootElement
            .GetProperty("ImageStorage")
            .GetProperty("RequestPath")
            .GetString();

        requestPath.Should().NotBeNullOrWhiteSpace();

        var gatewayPrefixes = GatewayRoutePrefixes();
        gatewayPrefixes.Should().Contain(
            prefix => (requestPath + "/").StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
            $"uploaded images are served from '{requestPath}' and must be forwarded by the gateway " +
            "for ImageStorage:PublicBaseUrl (the gateway origin) to resolve");
    }

    /// <summary>
    /// Resolves a controller-level route template to the concrete v1 path the SPA calls,
    /// e.g. "api/v{version:apiVersion}/[controller]" on ImagesController -> "/api/v1/images".
    /// </summary>
    private static string NormalizeControllerRoute(string? template, string controllerTypeName)
    {
        var controllerName = controllerTypeName.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerTypeName[..^"Controller".Length]
            : controllerTypeName;

        var path = (template ?? string.Empty)
            .Replace("{version:apiVersion}", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        return "/" + path;
    }

    /// <summary>
    /// Reads the YARP route patterns from the gateway's base appsettings.json and returns
    /// them as path prefixes with the catch-all stripped and the version parameter
    /// resolved to v1, e.g. "/api/v{version:int}/images/{**catch-all}" -> "/api/v1/images/".
    /// </summary>
    private static List<string> GatewayRoutePrefixes()
    {
        using var gatewaySettings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SolutionDirectory(), "API_Gateway", "appsettings.json")));

        var prefixes = new List<string>();
        foreach (var route in gatewaySettings.RootElement
                     .GetProperty("ReverseProxy")
                     .GetProperty("Routes")
                     .EnumerateObject())
        {
            var pattern = route.Value.GetProperty("Match").GetProperty("Path").GetString();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var prefix = pattern
                .Replace("{**catch-all}", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{version:int}", "1", StringComparison.OrdinalIgnoreCase);
            prefixes.Add(prefix.EndsWith('/') ? prefix : prefix + "/");
        }

        prefixes.Should().NotBeEmpty("the gateway must define ReverseProxy routes");
        return prefixes;
    }

    /// <summary>Walks up from the test bin directory to the directory containing Auth.sln.</summary>
    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
