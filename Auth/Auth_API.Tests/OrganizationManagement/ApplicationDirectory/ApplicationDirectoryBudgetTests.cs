using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth_API.Common.Authentication;
using Auth_API.Common.Middleware;
using Auth_API.Common.RateLimiting;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// The application directory's budget (OI-194, T11 with D-194-1 (a)) as a server meets it: the
/// real <see cref="ApplicationReadClassificationMiddleware"/> before the limiter, the real
/// scheme registration, and the "app-read" policy partitioned exactly as Program.cs does, on an
/// endpoint that names the scheme the way the directory controller does. Then the wiring itself,
/// read from the two Program.cs files, the settings and the gateway's routes.
/// </summary>
public sealed class ApplicationDirectoryBudgetTests : IAsyncLifetime
{
    private const string Path = "/api/v1/app/organizations/directory-probe";
    private const string Server = "203.0.113.7";
    private const string Elsewhere = "198.51.100.66";
    private const int PerApplication = 3;
    private const int PerAddress = 2;

    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddAuthorization();
                    services.AddRateLimiter(options =>
                    {
                        // As Program.cs registers it.
                        options.AddPolicy("app-read", httpContext =>
                            ApplicationReadClassificationMiddleware.Partition(
                                httpContext,
                                1,
                                perApplication: () => new FixedWindowBudget(PerApplication, 3600),
                                perAddress: () => new FixedWindowBudget(PerAddress, 3600)));
                        options.OnRejected = (context, _) =>
                        {
                            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return ValueTask.CompletedTask;
                        };
                    });
                })
                .Configure(app =>
                {
                    // ClientIpResolver reads X-Forwarded-For, as behind the gateway.
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<ApplicationReadClassificationMiddleware>();
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet(Path, () => Results.Ok())
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.UserInfoScheme })
                        .RequireRateLimiting(ApplicationReadClassificationMiddleware.PolicyName));
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _tokens.Dispose();
    }

    private async Task<HttpStatusCode> CallAsync(string? token, string address = Server)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add("X-Forwarded-For", address);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private string TokenFor(string application) => _tokens.ForApplication(_user, "openid organizations.read", audience: application);

    /// <summary>A well-formed token naming <paramref name="application"/>, signed with a key this server does not have.</summary>
    private static string Forged(string application)
    {
        using var rsa = RSA.Create(2048);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = UserInfoTokens.Issuer,
            Audience = application,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["scope"] = "openid organizations.read" },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256)
        });
    }

    [Fact]
    public async Task TwoApplicationsFromOneAddress_HaveSeparateBudgets_LargerThanTheAddresses()
    {
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("crm"))).Should().Be(HttpStatusCode.OK, "within the application's budget, above the address's");
        }

        (await CallAsync(TokenFor("crm"))).Should().Be(HttpStatusCode.TooManyRequests);

        // The other application behind the same address still has all of its own.
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("shop"))).Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AHolderOfAValidTokenElsewhere_SpendsOnlyTheirOwnAddresssShare_NeverTheServers()
    {
        // D-194-1: per application AND per address. A user holding one of the application's
        // tokens (an application that hands them to the browser) empties their own share...
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("crm"), Elsewhere)).Should().Be(HttpStatusCode.OK);
        }

        (await CallAsync(TokenFor("crm"), Elsewhere)).Should().Be(HttpStatusCode.TooManyRequests);

        // ...and the application's server, at its own address, still has all of its own.
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("crm"), Server)).Should().Be(HttpStatusCode.OK);
        }

        (await CallAsync(TokenFor("crm"), Server)).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ForgedTokensNamingAnApplication_CountOnTheirAddress_AndNeverSpendTheApplicationsBudget()
    {
        // From the application's own address, so only the partition tells them apart.
        for (var i = 0; i < PerAddress; i++)
        {
            (await CallAsync(Forged("crm"))).Should().Be(HttpStatusCode.Unauthorized);
        }

        (await CallAsync(Forged("crm"))).Should().Be(HttpStatusCode.TooManyRequests, "the address's small budget is spent");
        (await CallAsync(null)).Should().Be(HttpStatusCode.TooManyRequests, "a request with no token counts there too");

        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("crm"))).Should().Be(HttpStatusCode.OK, "the application's budget is untouched");
        }
    }

    [Fact]
    public async Task ValidTokensWithoutTheScope_CountOnTheirAddress_AndNeverSpendTheApplicationsBudget()
    {
        // A token the application was issued for something else (no organizations.read) is
        // refused by the endpoints anyway; it must not spend the budget their reads need.
        var withoutTheScope = _tokens.ForApplication(_user, "openid profile email", audience: "crm");

        for (var i = 0; i < PerAddress; i++)
        {
            (await CallAsync(withoutTheScope)).Should().Be(HttpStatusCode.OK, "the probe endpoint itself does not check the scope");
        }

        (await CallAsync(withoutTheScope)).Should().Be(HttpStatusCode.TooManyRequests, "the address's small budget is spent");

        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(TokenFor("crm"))).Should().Be(HttpStatusCode.OK, "the application's budget is untouched");
        }
    }

    // --- The wiring ---

    [Fact]
    public void TheDirectoryController_OptsIntoTheAppReadPolicy()
    {
        typeof(ApplicationOrganizationsController).GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should().Be(ApplicationReadClassificationMiddleware.PolicyName);
        ApplicationReadClassificationMiddleware.PolicyName.Should().Be("app-read");
    }

    [Fact]
    public void TheApi_RegistersThePolicyByItsName_ReadingBothPairs_OnTheirOwnArguments()
    {
        var program = ReadSource("Auth_API", "Program.cs");
        var body = PolicyBody(program, "app-read");

        body.Should().Contain("ApplicationReadClassificationMiddleware.Partition(");
        body.Should().Contain("settingsVersion()");
        body.Should().MatchRegex(
            @"perApplication: \(\) => new FixedWindowBudget\(\s*builder\.Configuration\.GetValue\(""RateLimiting:AppReadApplicationPermitLimit"", 300\),\s*builder\.Configuration\.GetValue\(""RateLimiting:AppReadApplicationWindowSeconds"", 60\)\)");
        body.Should().MatchRegex(
            @"perAddress: \(\) => new FixedWindowBudget\(\s*builder\.Configuration\.GetValue\(""RateLimiting:AppReadAddressPermitLimit"", 60\),\s*builder\.Configuration\.GetValue\(""RateLimiting:AppReadAddressWindowSeconds"", 60\)\)");
    }

    [Fact]
    public void TheClassification_RunsAfterAuthentication_AndBeforeTheLimiter()
    {
        var program = CodeOnly(ReadSource("Auth_API", "Program.cs"));

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var classification = program.IndexOf("app.UseMiddleware<ApplicationReadClassificationMiddleware>();", StringComparison.Ordinal);
        var limiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);

        classification.Should().BeGreaterThan(authentication);
        limiter.Should().BeGreaterThan(classification, "a partition is chosen when the limiter runs: the caller must be marked by then");
        Regex.Matches(program, @"UseMiddleware<ApplicationReadClassificationMiddleware>").Should().ContainSingle();
    }

    [Fact]
    public void TheGateway_ForwardsTheDirectory_OnItsOwnRoute_AndPolicy_ReadingItsOwnPair()
    {
        using var settings = JsonDocument.Parse(ReadSource("API_Gateway", "appsettings.json"));
        var route = settings.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("app-route");
        route.GetProperty("RateLimiterPolicy").GetString().Should().Be("app-read");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/v{version:int}/app/{**catch-all}");
        settings.RootElement.TryGetProperty("_app_route_comment", out _).Should().BeTrue();

        var body = PolicyBody(ReadSource("API_Gateway", "Program.cs"), "app-read");
        body.Should().Contain("limits.AppReadPermitLimit").And.Contain("limits.AppReadWindowSeconds");
        body.Should().Contain("SettingsVersion(context)").And.Contain("ClientId(context)");
    }

    [Theory]
    [InlineData("RateLimiting", "AppReadApplicationPermitLimit", 300)]
    [InlineData("RateLimiting", "AppReadApplicationWindowSeconds", 60)]
    [InlineData("RateLimiting", "AppReadAddressPermitLimit", 60)]
    [InlineData("RateLimiting", "AppReadAddressWindowSeconds", 60)]
    [InlineData("GatewayRateLimiting", "AppReadPermitLimit", 600)]
    [InlineData("GatewayRateLimiting", "AppReadWindowSeconds", 60)]
    public void EveryNewLimit_HasOneDefault_InTheRegistryAndTheApisFile(string section, string field, int expected)
    {
        var definition = SystemSettingsRegistry.TryGetField(SystemSettingsRegistry.TryGet(section)!, field);
        definition.Should().NotBeNull();
        Convert.ToInt32(definition!.DefaultValue).Should().Be(expected);

        using var api = JsonDocument.Parse(ReadSource("Auth_API", "appsettings.json"));
        api.RootElement.GetProperty(section).GetProperty(field).GetInt32().Should().Be(expected);
    }

    [Fact]
    public void TheEdgeDefault_ServesTwoApplicationsBehindOneAddress_UnderTheGlobalCeiling()
    {
        int Default(string section, string field) =>
            Convert.ToInt32(SystemSettingsRegistry.TryGetField(SystemSettingsRegistry.TryGet(section)!, field)!.DefaultValue);

        Default("GatewayRateLimiting", "AppReadPermitLimit")
            .Should().BeGreaterThanOrEqualTo(2 * Default("RateLimiting", "AppReadApplicationPermitLimit"))
            .And.BeLessThanOrEqualTo(Default("GatewayRateLimiting", "GlobalPermitLimit"));
    }

    #region Helpers

    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string PolicyBody(string program, string policy)
    {
        var start = program.IndexOf($"options.AddPolicy(\"{policy}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the {0} policy must be registered under its literal name", policy);
        return program[start..program.IndexOf("options.AddPolicy(", start + 1, StringComparison.Ordinal)];
    }

    private static string ReadSource(params string[] relativeParts)
        => File.ReadAllText(System.IO.Path.Combine(SolutionDirectory(), System.IO.Path.Combine(relativeParts)));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find Auth.sln above their output folder");
        return directory!.FullName;
    }

    #endregion
}
