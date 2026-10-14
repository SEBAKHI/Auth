using System.Net;
using System.Net.Http.Headers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Authentication;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// An application protected by the SDK refuses a machine token (OI-128, review F10). The machine
/// audience is one constant for every application's machine tokens, so what keeps them out is the
/// application's own audience, and the SDK refusing the machine one as an application's.
/// </summary>
/// <remarks>
/// The harness of <see cref="ScopeIsNotAPermissionTests"/>: real tokens from
/// <see cref="JwtTokenService"/>, validated by <c>AddAuthSystemAuthentication</c> in a real
/// pipeline, with the signing key supplied locally.
/// </remarks>
public sealed class MachineTokenAtAnSdkApplicationTests : IAsyncLifetime, IDisposable
{
    private const string Issuer = "https://auth.example.com";
    private const string Audience = "SHOP";

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
                    app.UseEndpoints(endpoints => endpoints.MapGet("/signed-in", () => "ok").RequireAuthorization());
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

    private async Task<HttpStatusCode> GetAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/signed-in");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task AMachineToken_IsRefused_WhereTheApplicationsOwnTokenIsAccepted()
    {
        // The control: the same issuer and key, the application's own audience, accepted. Without
        // it the 401 below could be a host that accepts nothing.
        var applicationToken = _issuer.GenerateAccessToken(
            TestHelpers.CreateUser(email: "user@example.com"),
            permissions: [],
            roles: [],
            authentication: AccessTokenAuthentication.Unrecorded,
            sessionId: Guid.NewGuid(),
            organizationPermissions: null,
            audience: Audience,
            scope: "openid");
        (await GetAsync(applicationToken)).Should().Be(HttpStatusCode.OK);

        // The application's own client id as the machine client, and every machine scope.
        var machineToken = _issuer.GenerateMachineAccessToken(Audience, MachineClients.Scopes.All);
        (await GetAsync(machineToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("urn:authsystem:machine")]
    [InlineData("URN:AuthSystem:Machine")]
    [InlineData(" urn:authsystem:machine/ ")]
    public void TheSdk_RefusesTheMachineAudienceAsAnApplicationsOwn(string audience)
    {
        var services = new ServiceCollection();

        var register = () => services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = Issuer;
            options.Issuer = Issuer;
            options.Audience = audience;
        });

        register.Should().Throw<InvalidOperationException>().WithMessage("*machine token audience*");
    }

    [Fact]
    public void TheSdksMachineAudience_IsTheServers()
    {
        AuthSystemConstants.MachineTokenAudience.Should().Be(MachineClients.Audience);
    }
}
