using Auth.Shared.Http;
using Auth_API.Common;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The preflight answer a browser may keep. Without <c>Access-Control-Max-Age</c>
/// Chromium forgets it after five seconds, so every sign-in page waited for one
/// more round trip in front of each API call (<see cref="CorsPreflight"/>).
/// </summary>
public class CorsPreflightMaxAgeTests
{
    private const string AllowedOrigin = "https://accounts.example.test";

    [Fact]
    public async Task ApiPreflight_TellsTheBrowserToKeepTheAnswerForTwoHours()
    {
        var response = await PreflightFrom(AllowedOrigin);

        response.Headers.AccessControlAllowOrigin.ToString().Should().Be(AllowedOrigin);
        response.Headers.AccessControlMaxAge.ToString().Should().Be("7200");
    }

    [Fact]
    public async Task ApiPreflight_FromAnOriginOffTheList_GivesNothingToKeep()
    {
        var response = await PreflightFrom("https://elsewhere.example.test");

        response.Headers.ContainsKey("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.ContainsKey("Access-Control-Max-Age").Should().BeFalse();
    }

    [Fact]
    public void GatewayPolicy_SendsTheSameMaxAge()
    {
        // The gateway is the host browsers call, and this project does not
        // reference it: its provider is held to the shared value by its source.
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "API_Gateway", "Configuration", "DynamicCorsPolicyProvider.cs"));

        source.Should().Contain(".SetPreflightMaxAge(CorsPreflight.MaxAge)",
            "the gateway answers the browser's preflights, so it must let the browser keep them");
    }

    private static async Task<HttpResponse> PreflightFrom(string origin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                ["Cors:AllowCredentials"] = "true",
            })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Production");

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Options;
        context.Request.Headers.Origin = origin;
        context.Request.Headers.AccessControlRequestMethod = "GET";
        context.Request.Headers.AccessControlRequestHeaders = "x-device-id";

        var policy = await new DynamicCorsPolicyProvider(configuration, environment.Object)
            .GetPolicyAsync(context, policyName: null);
        var cors = new CorsService(Options.Create(new CorsOptions()), NullLoggerFactory.Instance);
        cors.ApplyResult(cors.EvaluatePolicy(context, policy!), context.Response);

        return context.Response;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }
}
