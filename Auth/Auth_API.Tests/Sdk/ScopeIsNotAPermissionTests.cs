using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Authentication;
using Auth.Sdk;
using Auth.Sdk.Authorization;
using Auth.Sdk.Extensions;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// Every application token now carries the OAuth grant as a "scope" claim
/// ("openid", "openid profile", …). The SDK used to read "scope" as a permission
/// claim, so <c>[RequirePermission("openid")]</c> would have passed for every token of
/// every application (OI-58, B12).
/// </summary>
/// <remarks>
/// Tested in the consuming medium: a real token from <see cref="JwtTokenService"/>,
/// validated by the SDK's own registration (<c>AddAuthSystemAuthentication</c>) in a
/// real ASP.NET Core pipeline, against an endpoint guarded by the SDK attribute.
/// Only the signing key and the discovery document are supplied locally, so no
/// network is touched.
/// </remarks>
public sealed class ScopeIsNotAPermissionTests : IAsyncLifetime, IDisposable
{
    private const string Issuer = "https://auth.example.com";
    private const string Audience = "EDIS";

    private readonly JwtTokenService _issuer = new(
        Options.Create(new JwtSettings { Issuer = Issuer, Audience = "auth-platform", KeyId = "test-key" }),
        Mock.Of<IPasswordHasher>());

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddAuthSystemAuthentication(options =>
                    {
                        options.BaseUrl = Issuer;
                        options.Issuer = Issuer;
                        options.Audience = Audience;
                    });

                    // The discovery document the SDK would download, with the
                    // issuer's public key: a Configure, so it is in place before
                    // JwtBearer's post-configuration decides how to fetch metadata.
                    services.Configure<JwtBearerOptions>(AuthSystemConstants.BearerScheme, jwt =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                        configuration.SigningKeys.Add(_issuer.GetSecurityKey());
                        jwt.Configuration = configuration;
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/needs-openid", () => "ok")
                            .RequireAuthorization(new RequirePermissionAttribute("openid"));
                        endpoints.MapGet("/needs-users-read", () => "ok")
                            .RequireAuthorization(new RequirePermissionAttribute("users:read"));
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
    }

    public void Dispose() => _issuer.Dispose();

    private string ApplicationToken(string scope) => _issuer.GenerateAccessToken(
        TestHelpers.CreateUser(email: "user@example.com"),
        permissions: ["users:read"],
        roles: [],
        sessionId: Guid.NewGuid(),
        organizationPermissions: null,
        audience: Audience,
        scope: scope);

    private async Task<HttpStatusCode> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task AnApplicationToken_IsAcceptedByTheSdk_AndItsRealPermissionsStillCount()
    {
        // The control: the token validates and a permission it does hold passes.
        // Without this, the 403 below could be a token the SDK never accepted.
        (await GetAsync("/needs-users-read", ApplicationToken("openid"))).Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("openid")]
    [InlineData("openid profile email phone")]
    public async Task TheScopeClaim_DoesNotSatisfyRequirePermission(string scope)
    {
        // Authenticated (not 401) and refused (403): a scope is a grant, not a permission.
        (await GetAsync("/needs-openid", ApplicationToken(scope))).Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<bool> SatisfiesAsync(ClaimsPrincipal principal, string permission)
    {
        var handler = new PermissionRequirementHandler(
            Mock.Of<ILogger<PermissionRequirementHandler>>());
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    /// <summary>
    /// The principal the ApiKey handler builds: each key scope written twice, as
    /// "scope" and as "permission" (ApiKeyAuthenticationHandler).
    /// </summary>
    private static ClaimsPrincipal ApiKeyPrincipal(string keyScope) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim("apikey_id", Guid.NewGuid().ToString()),
                new Claim("scope", keyScope),
                new Claim("permission", keyScope),
            ],
            AuthSystemConstants.ApiKeyScheme));

    [Fact]
    public async Task AnApiKeyScope_StillSatisfiesRequirePermission_ThroughItsPermissionClaim()
    {
        (await SatisfiesAsync(ApiKeyPrincipal("crm:leads:read"), "crm:leads:read")).Should().BeTrue();
        (await SatisfiesAsync(ApiKeyPrincipal("crm:*"), "crm:leads:read")).Should().BeTrue();
    }
}
