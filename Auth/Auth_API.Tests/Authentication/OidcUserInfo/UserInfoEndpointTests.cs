using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Features.Authentication.GetOidcUserInfo;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Authorization;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using Auth_API.Modules.ApplicationManagement.Controllers;
using Auth_API.Modules.Authentication.Controllers;
using Auth_Localization.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.Authentication.OidcUserInfo;

/// <summary>
/// The userinfo endpoint as a relying party meets it (X11, R1-R4): the REAL AuthController, the
/// SAME scheme registration Program.cs calls, the real blacklist middleware in Program.cs's
/// position, the real query handler behind a real mediator, and a stub repository holding one
/// user with a phone, a picture and a confirmed email. Every token is minted by the real token
/// service. What is asserted is the wire: status, headers, problem code and JSON members.
/// </summary>
public sealed class UserInfoEndpointTests : IAsyncLifetime
{
    private const string UserInfoPath = "/api/v1/auth/userinfo";
    private const string MePath = "/api/v1/auth/me";
    private const string PermissionGuardedPath = "/api/v1/applications";
    private const string Phone = "+971 50 123 4567";

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IImageUrlComposer> _images = new();
    private readonly User _user;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public UserInfoEndpointTests()
    {
        _user = User.Create(
            email: "user@example.com",
            passwordHash: "hash",
            firstName: "Test",
            lastName: "User",
            createdBy: Guid.Empty,
            phoneNumber: Phone,
            timeZone: "Asia/Dubai");
        _user.ConfirmEmail(Guid.Empty);
        _user.SetProfileImage("avatars/user.png", Guid.Empty);

        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _images.Setup(c => c.Compose("avatars/user.png")).Returns("https://auth.example.com/uploads/images/avatars/user.png");
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

                    // The registrations Program.cs makes for this path.
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
                    services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
                    services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaForbiddenResultHandler>();
                    services.AddAuthorization();
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetOidcUserInfoQueryHandler>());
                    services.AddSingleton(_users.Object);
                    services.AddSingleton(_images.Object);
                    services.AddSingleton(_blacklist.Object);

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
                            parts.FeatureProviders.Add(new TheseControllersOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        // As Program.cs: the versioning filter adds api-supported-versions to
                        // every response MVC writes, which a 401 from middleware never carries.
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
        _tokens.Dispose();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string WwwAuthenticate(HttpResponseMessage response) =>
        string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

    private static IReadOnlyList<string> Members(JsonElement body) =>
        body.EnumerateObject().Select(p => p.Name).ToList();

    // --- R1: an application token reads the profile its scopes allow ---

    [Fact]
    public async Task UserInfo_ApplicationTokenWithOpenIdOnly_Returns200WithTheSubjectOnly()
    {
        var response = await SendAsync(HttpMethod.Get, UserInfoPath, _tokens.ForApplication(_user, "openid"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        // The user has a phone, a picture, a name and a confirmed email; none is granted.
        Members(body).Should().Equal("sub");
        body.GetProperty("sub").GetString().Should().Be(_user.Id.ToString());
    }

    public static TheoryData<string> Methods => new() { "GET", "POST" };

    [Fact]
    public void Methods_IsNotEmpty() => Methods.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task UserInfo_ApplicationTokenWithEveryScope_Returns200WithTheProfile(string method)
    {
        var token = _tokens.ForApplication(_user, "openid profile email phone");

        var response = await SendAsync(new HttpMethod(method), UserInfoPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await JsonAsync(response);
        Members(body).Should().BeEquivalentTo(
            "sub", "name", "given_name", "family_name", "locale", "zoneinfo", "picture",
            "email", "email_verified", "phone_number", "phone_number_verified");
        body.GetProperty("sub").GetString().Should().Be(_user.Id.ToString());
        body.GetProperty("phone_number").GetString().Should().Be(Phone);
        body.GetProperty("phone_number_verified").ValueKind.Should().Be(JsonValueKind.False);
        body.GetProperty("email_verified").ValueKind.Should().Be(JsonValueKind.True);
        body.GetProperty("picture").GetString().Should().Be("https://auth.example.com/uploads/images/avatars/user.png");
        // The token carries all of these (UserInfoTokens.ForApplication); none comes back.
        Members(body).Should().NotContain(["roles", "permissions", "org_perm", "org_id", "org_name", "scope", "sid", "jti"]);
    }

    [Fact]
    public async Task UserInfo_PostWithAnAccessTokenFormField_IgnoresTheField()
    {
        // RFC 6750 §2.2 is optional for a server, and a body token would skip the blacklist.
        var form = new FormUrlEncodedContent([new("access_token", _tokens.ForApplication(_user, "openid"))]);

        var response = await SendAsync(HttpMethod.Post, UserInfoPath, token: null, form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task UserInfo_TokenInTheQueryString_Returns401()
    {
        var token = _tokens.ForApplication(_user, "openid");

        var response = await SendAsync(HttpMethod.Get, $"{UserInfoPath}?access_token={token}", token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
    }

    // --- R2: an application token opens userinfo and nothing else; a platform token the reverse ---

    [Fact]
    public async Task UserInfo_PlatformToken_Returns401_WhileMeAcceptsIt()
    {
        var token = _tokens.ForPlatform(_user);

        var userInfo = await SendAsync(HttpMethod.Get, UserInfoPath, token);
        var me = await SendAsync(HttpMethod.Get, MePath, token);

        userInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Written by the OidcUserInfo challenge, and by it alone: one header, the transport code.
        userInfo.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
        (await ProblemCodeAsync(userInfo)).Should().Be(TransportErrorCodes.Unauthenticated);
        me.StatusCode.Should().Be(HttpStatusCode.OK, "the console's own read is unchanged");
    }

    [Fact]
    public async Task Me_ApplicationToken_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, MePath, _tokens.ForApplication(_user, "openid profile email phone"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task PermissionGuardedAction_ApplicationToken_Returns401_PlatformTokenWithoutThePermission_Returns403()
    {
        // 403 for the platform token proves the default scheme authenticated it there; the
        // application token is not even authenticated.
        var application = await SendAsync(HttpMethod.Get, PermissionGuardedPath, _tokens.ForApplication(_user, "openid"));
        var platform = await SendAsync(HttpMethod.Get, PermissionGuardedPath, _tokens.ForPlatform(_user));

        application.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        platform.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // --- S08: a platform token whose authority is withheld for a missing second factor ---

    [Fact]
    public async Task PermissionGuardedAction_WithheldPlatformToken_Returns403WithRequiredByPolicy()
    {
        var response = await SendAsync(
            HttpMethod.Get, PermissionGuardedPath,
            _tokens.ForPlatformWithheld(_user, Auth.Domain.Enums.MfaRequirement.StepUp));

        // 403, not 401: the console refreshes on 401, and the refresh mints the same token.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ProblemCodeAsync(response)).Should().Be(Auth.Domain.Errors.TwoFactorErrors.RequiredByPolicy.Code);
        WwwAuthenticate(response).Should().Contain("insufficient_user_authentication",
            "RFC 9470 §3 names the reason in the challenge");
    }

    [Fact]
    public async Task PermissionGuardedAction_PlatformTokenWithoutMfaReq_KeepsTheTransportForbidden()
    {
        var response = await SendAsync(HttpMethod.Get, PermissionGuardedPath, _tokens.ForPlatform(_user));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be(TransportErrorCodes.Forbidden,
            "only a token carrying mfa_req is answered with the two-factor code");
        WwwAuthenticate(response).Should().NotContain("insufficient_user_authentication");
    }

    [Theory]
    [InlineData(Auth.Domain.Enums.MfaRequirement.Enroll, "enroll")]
    [InlineData(Auth.Domain.Enums.MfaRequirement.StepUp, "step_up")]
    [InlineData(Auth.Domain.Enums.MfaRequirement.Reauthenticate, "reauthenticate")]
    public async Task Me_WithheldPlatformToken_EchoesTheRequirement(Auth.Domain.Enums.MfaRequirement requirement, string value)
    {
        var response = await SendAsync(HttpMethod.Get, MePath, _tokens.ForPlatformWithheld(_user, requirement));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "/me stays open: the console reads the requirement there");
        var body = await JsonAsync(response);
        body.GetProperty("mfaRequirement").GetString().Should().Be(value);
        body.GetProperty("permissions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Me_PlatformToken_SaysNone()
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get, MePath, _tokens.ForPlatform(_user)));

        body.GetProperty("mfaRequirement").GetString().Should().Be("none");
    }

    // --- R3 and R1b: refused tokens and subjects, with the bearer contract's answer ---

    [Fact]
    public async Task UserInfo_RevokedApplicationToken_Returns401WithInvalidToken()
    {
        var token = _tokens.ForApplication(_user, "openid profile email phone");
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(token)!)).Returns(true);

        var response = await SendAsync(HttpMethod.Get, UserInfoPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(response).Should().Be("Bearer error=\"invalid_token\"");
        (await ProblemCodeAsync(response)).Should().Be(ChallengeReasonCodes.TokenRevoked);
    }

    [Fact]
    public async Task UserInfo_ApplicationTokenOfARevokedSession_Returns401WithInvalidToken()
    {
        var session = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionBlacklisted(session.ToString())).Returns(true);

        var response = await SendAsync(
            HttpMethod.Get, UserInfoPath, _tokens.ForApplication(_user, "openid", sessionId: session));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(response).Should().Be("Bearer error=\"invalid_token\"");
        (await ProblemCodeAsync(response)).Should().Be(ChallengeReasonCodes.SessionRevoked);
    }

    [Fact]
    public async Task UserInfo_DeletedUser_Returns401LikeARevokedToken_WithNoProfile()
    {
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var response = await SendAsync(HttpMethod.Get, UserInfoPath, _tokens.ForApplication(_user, "openid profile email phone"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(response).Should().Be("Bearer error=\"invalid_token\"");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await JsonAsync(response);
        body.GetProperty("code").GetString().Should().Be(ChallengeReasonCodes.TokenRevoked);
        Members(body).Should().NotContain(["sub", "email", "phone_number", "name"]);
    }

    [Fact]
    public async Task UserInfo_DeletedUserAndRevokedToken_AreIndistinguishable()
    {
        // R1b: the caller must not learn that the account, rather than the token, is gone. The
        // first 401 is written inside MVC, the second by middleware before MVC; every header and
        // the body must still match.
        var deletedUser = User.Create("gone@example.com", "hash", "Gone", "User", Guid.Empty);
        var revokedToken = _tokens.ForApplication(_user, "openid profile email phone");
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revokedToken)!)).Returns(true);
        _users.Setup(r => r.GetByIdAsync(deletedUser.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var unservable = await SendAsync(HttpMethod.Get, UserInfoPath, _tokens.ForApplication(deletedUser, "openid profile email phone"));
        var revoked = await SendAsync(HttpMethod.Get, UserInfoPath, revokedToken);

        static IEnumerable<string> HeaderNames(HttpResponseMessage response) =>
            response.Headers.Concat(response.Content.Headers)
                .Select(h => h.Key.ToLowerInvariant())
                .Where(name => name is not "date" and not "content-length")
                .Order();

        unservable.StatusCode.Should().Be(revoked.StatusCode);
        HeaderNames(unservable).Should().Equal(HeaderNames(revoked));
        WwwAuthenticate(unservable).Should().Be(WwwAuthenticate(revoked));
        var unservableBody = await JsonAsync(unservable);
        var revokedBody = await JsonAsync(revoked);
        unservableBody.GetProperty("code").GetString().Should().Be(revokedBody.GetProperty("code").GetString());
        Members(unservableBody).Should().Equal(Members(revokedBody));
    }

    [Fact]
    public async Task UserInfo_ExpiredApplicationToken_Returns401WithTheCodeTheDefaultSchemeGives()
    {
        var expired = _tokens.Custom(_user.Id, descriptor =>
        {
            descriptor.IssuedAt = DateTime.UtcNow.AddMinutes(-30);
            descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-30);
            descriptor.Expires = DateTime.UtcNow.AddMinutes(-10);
        });
        var expiredPlatform = _tokens.Custom(_user.Id, descriptor =>
        {
            descriptor.Audience = _tokens.Settings.Audience;
            descriptor.IssuedAt = DateTime.UtcNow.AddMinutes(-30);
            descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-30);
            descriptor.Expires = DateTime.UtcNow.AddMinutes(-10);
        });

        var userInfo = await SendAsync(HttpMethod.Get, UserInfoPath, expired);
        var me = await SendAsync(HttpMethod.Get, MePath, expiredPlatform);

        userInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(userInfo)).Should().Be(ChallengeReasonCodes.TokenExpired);
        (await ProblemCodeAsync(me)).Should().Be(ChallengeReasonCodes.TokenExpired);
        userInfo.Headers.GetValues("Token-Expired").Should().Equal("true");
    }

    [Fact]
    public async Task UserInfo_NoToken_Returns401()
    {
        var response = await SendAsync(HttpMethod.Get, UserInfoPath, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        WwwAuthenticate(response).Should().StartWith("Bearer");
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class TheseControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(AuthController).GetTypeInfo());
            feature.Controllers.Add(typeof(ApplicationsController).GetTypeInfo());
        }
    }
}
