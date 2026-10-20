using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Auth.Domain.Constants;
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
/// The machine directory's budget (OI-202, T11, V6): the same <c>app-read</c> policy and settings
/// as the user directory, partitioned by the machine profile. Two probe endpoints in the
/// directory's shape: one naming the userinfo scheme, one naming the machine scheme and carrying
/// the machine profile, as <see cref="ApplicationMachineOrganizationsController"/> does.
/// </summary>
public sealed class MachineDirectoryBudgetTests : IAsyncLifetime
{
    private const string UserPath = "/api/v1/app/organizations/directory-probe";
    private const string MachinePath = "/api/v1/app/machine/organizations/directory-probe";
    private const string Server = "203.0.113.7";
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
                        // As Program.cs registers it: one policy for both doors.
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
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<ApplicationReadClassificationMiddleware>();
                    app.UseRateLimiter();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints
                            .MapGet(UserPath, () => Results.Ok())
                            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.UserInfoScheme })
                            .RequireRateLimiting(ApplicationReadClassificationMiddleware.PolicyName);
                        endpoints
                            .MapGet(MachinePath, () => Results.Ok())
                            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.MachineScheme })
                            .RequireRateLimiting(ApplicationReadClassificationMiddleware.PolicyName)
                            .WithMetadata(new MachineApplicationReadAttribute());
                    });
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

    private async Task<HttpStatusCode> CallAsync(string path, string? token, string address = Server)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", address);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private string MachineToken(string application, string scope = MachineClients.Scopes.OrganizationsReadAll) =>
        _tokens.Service.GenerateMachineAccessToken(application, [scope]);

    /// <summary>An unsigned machine-shaped token naming <paramref name="application"/>.</summary>
    private static string Unsigned(string application) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = UserInfoTokens.Issuer,
            Audience = MachineClients.Audience,
            TokenType = "at+jwt",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = application,
                ["client_id"] = application,
                ["jti"] = Guid.NewGuid().ToString(),
                ["scope"] = MachineClients.Scopes.OrganizationsReadAll,
            },
        });

    [Fact]
    public async Task TwoApplicationsMachineReadsFromOneAddress_HaveSeparateBudgets()
    {
        // Both tokens carry the same audience, the one machine audience: only client_id tells
        // the applications apart. Counting on aud would put both in one budget.
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.OK, "within the application's budget, above the address's");
        }

        (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.TooManyRequests);

        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(MachinePath, MachineToken("shop"))).Should().Be(HttpStatusCode.OK, "the other application has all of its own");
        }
    }

    [Fact]
    public async Task AMachineAndAUserReadOfOneApplication_CountApart()
    {
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.OK);
        }

        (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.TooManyRequests);

        var userToken = _tokens.ForApplication(_user, "openid organizations.read", audience: "crm");
        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(UserPath, userToken)).Should().Be(HttpStatusCode.OK, "the user reads of the same application have their own partition");
        }
    }

    [Fact]
    public async Task UnsignedMachineTokensNamingAnApplication_CountOnTheirAddress_AndNeverSpendItsBudget()
    {
        for (var i = 0; i < PerAddress; i++)
        {
            (await CallAsync(MachinePath, Unsigned("crm"))).Should().Be(HttpStatusCode.Unauthorized);
        }

        (await CallAsync(MachinePath, Unsigned("crm"))).Should().Be(HttpStatusCode.TooManyRequests, "the address's small budget is spent");

        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.OK, "the application's budget is untouched");
        }
    }

    [Fact]
    public async Task MachineTokensWithoutTheScope_CountOnTheirAddress()
    {
        var withoutTheScope = MachineToken("crm", MachineClients.Scopes.TokensIntrospect);

        for (var i = 0; i < PerAddress; i++)
        {
            (await CallAsync(MachinePath, withoutTheScope)).Should().Be(HttpStatusCode.OK, "the probe endpoint itself does not check the scope");
        }

        (await CallAsync(MachinePath, withoutTheScope)).Should().Be(HttpStatusCode.TooManyRequests);

        for (var i = 0; i < PerApplication; i++)
        {
            (await CallAsync(MachinePath, MachineToken("crm"))).Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AUserTokenWithTheScope_OnTheMachineDoor_NeverReachesAnApplicationsBudget()
    {
        // The machine door authenticates with the machine scheme only: a user's application token
        // fails it, and so counts on its address.
        var userToken = _tokens.ForApplication(_user, "openid organizations.read", audience: "crm");
        for (var i = 0; i < PerAddress; i++)
        {
            (await CallAsync(MachinePath, userToken)).Should().Be(HttpStatusCode.Unauthorized);
        }

        (await CallAsync(MachinePath, userToken)).Should().Be(HttpStatusCode.TooManyRequests);
    }

    // --- The wiring ---

    [Fact]
    public void TheMachineController_OptsIntoTheSamePolicy_WithTheMachineProfile()
    {
        var type = typeof(ApplicationMachineOrganizationsController);
        type.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(ApplicationReadClassificationMiddleware.PolicyName);
        type.GetCustomAttributes().OfType<IApplicationReadProfileMetadata>()
            .Should().ContainSingle().Which.Profile.Should().Be(ApplicationReadProfile.Machine);
        typeof(ApplicationOrganizationsController).GetCustomAttributes().OfType<IApplicationReadProfileMetadata>()
            .Should().BeEmpty("the user door keeps the default profile");
    }

    [Fact]
    public void TheProfiles_NameTheApplicationByTheRightClaim_AndTagsThatCannotCollide()
    {
        ApplicationReadProfile.UserThroughApplication.Should().Be(
            new ApplicationReadProfile(JwtClaimNames.Audience, OAuthScopes.OrganizationsRead, "app"));
        ApplicationReadProfile.Machine.Should().Be(
            new ApplicationReadProfile(JwtClaimNames.ClientId, MachineClients.Scopes.OrganizationsReadAll, "mach"));
        foreach (var tag in new[] { ApplicationReadProfile.UserThroughApplication.PartitionTag, ApplicationReadProfile.Machine.PartitionTag })
        {
            tag.Should().NotContain("|").And.NotContain(":");
        }
    }
}
