using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.GetOidcUserInfo;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// An application's token dies when it is revoked (OI-65), on the wire: the REAL AuthController
/// (revoke and userinfo), the SAME bearer registration Program.cs calls (and with it the token
/// validator), the real revoke handler behind a real mediator, the REAL credential-revocation
/// service, and a REAL <see cref="TokenBlacklistService"/> over an unbounded channel, read by the
/// real blacklist middleware in Program.cs's position. Only the repositories are stubs. So what
/// is asserted is the whole chain: the handler writes, the middleware refuses.
/// </summary>
public sealed class AppTokenRevocationEndToEndTests : IAsyncLifetime
{
    private const string UserInfoPath = "/api/v1/auth/userinfo";
    private const string RevokePath = "/api/v1/auth/revoke";

    private readonly UserInfoTokens _tokens = new();
    private readonly TokenBlacklistService _blacklist = new(
        Channel.CreateUnbounded<TokenRevocation>().Writer, NullLogger<TokenBlacklistService>.Instance);
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly User _user;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public AppTokenRevocationEndToEndTests()
    {
        _user = User.Create(
            email: "user@example.com",
            passwordHash: "hash",
            firstName: "Test",
            lastName: "User",
            createdBy: Guid.Empty);
        _user.ConfirmEmail(Guid.Empty);

        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _keys.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns((string plain) => $"hash:{plain}");
    }

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddLogging();
                    services.Configure<IdentityProviderSettings>(_ => { });

                    // The registrations Program.cs makes for these two paths.
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddAuthorization();
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetOidcUserInfoQueryHandler>());
                    services.AddSingleton<ITokenBlacklistService>(_blacklist);
                    services.AddScoped<ICredentialRevocationService, CredentialRevocationService>();
                    services.AddSingleton<IOptionsMonitor<JwtSettings>>(TestHelpers.CreateOptions(_tokens.Settings));
                    services.AddSingleton<IJwtTokenService>(_tokens.Service);
                    services.AddSingleton(_users.Object);
                    services.AddSingleton(_refreshTokens.Object);
                    services.AddSingleton(_sessions.Object);
                    services.AddSingleton(new Mock<IIdpSessionRepository>().Object);
                    services.AddSingleton(_keys.Object);
                    services.AddSingleton(new Mock<IImageUrlComposer>().Object);

                    services.AddControllers()
                        .AddJsonOptions(options =>
                        {
                            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                        })
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new AuthControllerOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        options.ReportApiVersions = true;
                        options.ApiVersionReader = new UrlSegmentApiVersionReader();
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    // Program.cs's order: error contract, authentication, blacklist, authorization.
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<JwtBlacklistValidationMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _blacklist.Dispose();
        _tokens.Dispose();
    }

    private Task<HttpResponseMessage> UserInfoAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    /// <summary>The revocation call as RFC 7009 has it: anonymous, form-encoded.</summary>
    private Task<HttpResponseMessage> RevokeAsync(string token, string hint) =>
        _client.PostAsync(RevokePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = hint,
        }));

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Revoke_ApplicationAccessToken_ThenUserInfo_Returns401WithTokenRevoked()
    {
        var sessionId = Guid.NewGuid();
        var token = _tokens.ForApplication(_user, "openid profile", sessionId: sessionId);
        var sibling = _tokens.ForApplication(_user, "openid", sessionId: sessionId);
        (await UserInfoAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK, "the token works before it is revoked");

        var revoke = await RevokeAsync(token, "access_token");

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await UserInfoAsync(token);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(after)).Should().Be(ChallengeReasonCodes.TokenRevoked);
        string.Join(", ", after.Headers.WwwAuthenticate.Select(h => h.ToString()))
            .Should().Contain("invalid_token");
        // That token and nothing more: its session, and a sibling token of it, stay up.
        (await UserInfoAsync(sibling)).StatusCode.Should().Be(HttpStatusCode.OK);
        _sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Revoke_RefreshTokenWithASession_EndsIt_SoItsAccessTokenGets401WithSessionRevoked()
    {
        var revokedSession = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        var token = _tokens.ForApplication(_user, "openid", sessionId: revokedSession);
        var otherToken = _tokens.ForApplication(_user, "openid", sessionId: otherSession);
        _refreshTokens
            .Setup(r => r.GetByTokenHashAsync("hash:plain-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: _user.Id, sessionId: revokedSession));

        var revoke = await RevokeAsync("plain-refresh", "refresh_token");

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        _sessions.Verify(
            s => s.TerminateAsync(revokedSession, TokenRevocationReasons.RevocationRequested, It.IsAny<CancellationToken>()),
            Times.Once());
        _refreshTokens.Verify(
            r => r.RevokeBySessionIdAsync(
                revokedSession, It.IsAny<Guid?>(), TokenRevocationReasons.RevocationRequested, It.IsAny<CancellationToken>()),
            Times.Once());
        var after = await UserInfoAsync(token);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(after)).Should().Be(ChallengeReasonCodes.SessionRevoked);
        // The same user's other session keeps working.
        (await UserInfoAsync(otherToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SwitchingTheApplicationOff_BlacklistsItsSessions_SoItsAccessTokenGets401WithSessionRevoked()
    {
        // The primitive the switch-off and the access removal call, through the host's own
        // registrations: what it ends, the middleware refuses.
        var applicationId = Guid.NewGuid();
        var endedSession = Guid.NewGuid();
        var token = _tokens.ForApplication(_user, "openid", sessionId: endedSession);
        var otherApplicationToken = _tokens.ForApplication(_user, "openid", audience: "other-app", sessionId: Guid.NewGuid());
        _sessions
            .Setup(s => s.TerminateForApplicationAsync(applicationId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([endedSession]);

        using (var scope = _host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICredentialRevocationService>()
                .TerminateApplicationSessionsAsync(
                    applicationId, userId: null, Guid.NewGuid(),
                    TokenRevocationReasons.ApplicationDeactivated, CancellationToken.None);
        }

        var after = await UserInfoAsync(token);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(after)).Should().Be(ChallengeReasonCodes.SessionRevoked);
        (await UserInfoAsync(otherApplicationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class AuthControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(AuthController).GetTypeInfo());
        }
    }
}
