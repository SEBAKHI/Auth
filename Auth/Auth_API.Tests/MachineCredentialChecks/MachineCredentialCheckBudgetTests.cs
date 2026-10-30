using System.Net;
using System.Net.Http.Headers;
using Auth.Domain.Constants;
using Auth_API.Common.Authentication;
using Auth_API.Common.Middleware;
using Auth_API.Common.RateLimiting;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.MachineCredentialChecks;

/// <summary>
/// The machine credential checks' budget (OI-128 PR B, B5): the <c>apikey-validate</c> policy,
/// partitioned as Program.cs partitions it, per validated <c>client_id</c> AND per address, decided
/// before the limiter by the classification step with each action's own scope. Probe endpoints in
/// the checks' shape (the machine scheme, the policy, the check's profile), beside a machine
/// directory probe, so the two budgets are shown apart.
/// </summary>
public sealed class MachineCredentialCheckBudgetTests : IAsyncLifetime
{
    private const string ApiKeyPath = "/api/v1/app/machine/apikeys/validate-probe";
    private const string IntrospectPath = "/api/v1/app/machine/tokens/introspect-probe";
    private const string DirectoryPath = "/api/v1/app/machine/organizations/directory-probe";
    private const string Server = "203.0.113.7";
    private const int Limit = 3;

    private readonly UserInfoTokens _tokens = new();
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
                        // As Program.cs registers it: both partitions read the same two settings.
                        options.AddPolicy("apikey-validate", httpContext =>
                            ApplicationReadClassificationMiddleware.Partition(
                                httpContext,
                                1,
                                perApplication: () => new FixedWindowBudget(Limit, 3600),
                                perAddress: () => new FixedWindowBudget(Limit, 3600)));
                        options.AddPolicy("app-read", httpContext =>
                            ApplicationReadClassificationMiddleware.Partition(
                                httpContext,
                                1,
                                perApplication: () => new FixedWindowBudget(Limit, 3600),
                                perAddress: () => new FixedWindowBudget(Limit, 3600)));
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
                        var machine = new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.MachineScheme };
                        endpoints
                            .MapPost(ApiKeyPath, () => Results.Ok())
                            .RequireAuthorization(machine)
                            .RequireRateLimiting(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName)
                            .WithMetadata(new MachineCredentialCheckAttribute(MachineClients.Scopes.ApiKeysValidate));
                        endpoints
                            .MapPost(IntrospectPath, () => Results.Ok())
                            .RequireAuthorization(machine)
                            .RequireRateLimiting(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName)
                            .WithMetadata(new MachineCredentialCheckAttribute(MachineClients.Scopes.TokensIntrospect));
                        endpoints
                            .MapPost(DirectoryPath, () => Results.Ok())
                            .RequireAuthorization(machine)
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
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Forwarded-For", address);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task SpendAsync(string path, string? token, int times, string address = Server)
    {
        for (var i = 0; i < times; i++)
        {
            await CallAsync(path, token, address);
        }
    }

    private string MachineToken(string application, params string[] scopes) =>
        _tokens.Service.GenerateMachineAccessToken(application, scopes.Length == 0 ? MachineClients.Scopes.All : scopes);

    /// <summary>A machine-shaped token naming <paramref name="application"/> that this issuer never signed.</summary>
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
                ["scope"] = string.Join(' ', MachineClients.Scopes.All),
            },
        });

    [Fact]
    public async Task TwoApplicationsBehindOneAddress_CountApart()
    {
        await SpendAsync(ApiKeyPath, MachineToken("CRM"), Limit);

        (await CallAsync(ApiKeyPath, MachineToken("CRM"))).Should().Be(HttpStatusCode.TooManyRequests);
        (await CallAsync(ApiKeyPath, MachineToken("ORDERS"))).Should().Be(HttpStatusCode.OK,
            "another application behind the same address has its own budget");
    }

    [Fact]
    public async Task OneApplication_CountsPerAddress()
    {
        await SpendAsync(ApiKeyPath, MachineToken("CRM"), Limit);

        (await CallAsync(ApiKeyPath, MachineToken("CRM"), "198.51.100.9")).Should().Be(HttpStatusCode.OK,
            "the application's budget at another address is untouched");
    }

    [Fact]
    public async Task TheThreeChecks_ShareTheApplicationsBudget()
    {
        await SpendAsync(ApiKeyPath, MachineToken("CRM"), Limit);

        (await CallAsync(IntrospectPath, MachineToken("CRM"))).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task UnsignedTokensNamingAnApplication_NeverReachItsBudget()
    {
        // Anyone can write a token that names CRM; the flood counts on its own address.
        await SpendAsync(ApiKeyPath, Unsigned("CRM"), Limit + 2);

        (await CallAsync(ApiKeyPath, Unsigned("CRM"))).Should().Be(HttpStatusCode.TooManyRequests);
        (await CallAsync(ApiKeyPath, MachineToken("CRM"))).Should().Be(HttpStatusCode.OK,
            "CRM's own server, at the same address, still has its whole budget");
    }

    [Fact]
    public async Task ATokenWithoutTheChecksScope_CountsOnTheAddress()
    {
        var noScope = MachineToken("CRM", MachineClients.Scopes.TokensIntrospect);
        await SpendAsync(ApiKeyPath, noScope, Limit);

        (await CallAsync(ApiKeyPath, null)).Should().Be(HttpStatusCode.TooManyRequests,
            "the token without apikeys.validate spent the address's budget, which an anonymous call shares");
        (await CallAsync(ApiKeyPath, MachineToken("CRM"))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheChecksBudget_NeverSharesAPartitionWithTheDirectorys()
    {
        await SpendAsync(ApiKeyPath, MachineToken("CRM", MachineClients.Scopes.ApiKeysValidate, MachineClients.Scopes.OrganizationsReadAll), Limit);

        (await CallAsync(DirectoryPath, MachineToken("CRM", MachineClients.Scopes.OrganizationsReadAll))).Should().Be(HttpStatusCode.OK);
        ApplicationReadProfile.MachineCredentialCheck(MachineClients.Scopes.ApiKeysValidate).PartitionTag
            .Should().NotBe(ApplicationReadProfile.Machine.PartitionTag)
            .And.NotBe(ApplicationReadProfile.UserThroughApplication.PartitionTag);
    }
}
