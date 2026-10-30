using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Behaviors;
using Auth.Application.Configuration;
using Auth.Application.Features.ApiKeys.ValidateApiKey;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Services;
using Auth.Domain.ValueObjects;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Authorization;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using Auth_API.Modules.ApiKeyManagement.Controllers;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Modules.MachineCredentialChecks.Controllers;
using Auth_API.Modules.WebhookKeyManagement.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.MachineCredentialChecks;

/// <summary>
/// The machine credential checks (OI-128 PR B) as an application's server meets them: the REAL
/// <see cref="MachineCredentialChecksController"/>, the SAME scheme registration Program.cs calls,
/// the classification step, the limiter and the blacklist in Program.cs's order, the real handlers
/// behind a real mediator with the validation behaviour, and stub repositories. Every token is
/// minted by the real token service. What is asserted is the wire: status, headers, problem code.
/// The old user-token doors' controllers are in the host too, so their paths are proven gone.
/// </summary>
public sealed class MachineCredentialChecksEndpointTests : IAsyncLifetime
{
    private const string AppCode = "SHOP";
    private const string OtherCode = "OTHER";
    private const string Selector = "Sel3ctor0000000A";

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApiKeyRepository> _apiKeys = new();
    private readonly Mock<IWebhookKeyRepository> _webhookKeys = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeCredentialHasher _credentialHasher = new();

    private readonly AppEntity _application = TestHelpers.CreateApplication(code: AppCode);
    private readonly AppEntity _other = TestHelpers.CreateApplication(code: OtherCode);
    private readonly User _user = TestHelpers.CreateUser();
    private readonly string _rawKey = ApiKeyFormat.Compose("production", Selector, new string('s', 32));
    private ApiKey _apiKey = null!;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _apiKey = TestHelpers.CreateApiKey(applicationId: _application.Id, keySelector: Selector, keyHash: "stored");
        _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.GetByCodeAsync(OtherCode, It.IsAny<CancellationToken>())).ReturnsAsync(_other);
        _apiKeys.Setup(r => r.GetActiveBySelectorAsync(Selector, It.IsAny<CancellationToken>())).ReturnsAsync(() => _apiKey);
        _apiKeys.Setup(r => r.GetScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(["shop:orders:read"]);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string hash) => key == _rawKey && hash == "stored");

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
                    services.AddMediatR(cfg =>
                    {
                        cfg.RegisterServicesFromAssemblyContaining<ValidateApiKeyQueryHandler>();
                        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    });
                    services.AddValidatorsFromAssemblyContaining<ValidateApiKeyQueryHandler>();
                    services.AddSingleton(_applications.Object);
                    services.AddSingleton(_apiKeys.Object);
                    services.AddSingleton(_webhookKeys.Object);
                    services.AddSingleton(_refreshTokens.Object);
                    services.AddSingleton(_hasher.Object);
                    services.AddSingleton(_blacklist.Object);
                    services.AddSingleton(_users.Object);
                    services.AddSingleton<ICredentialHasher>(_credentialHasher);
                    services.AddRateLimiter(options => options.AddPolicy(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName,
                        _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("unlimited")));

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
                        options.ReportApiVersions = true;
                        options.ApiVersionReader = new UrlSegmentApiVersionReader();
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    // Program.cs's order: error contract, authentication, classification, limiter,
                    // blacklist, authorization.
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<ApplicationReadClassificationMiddleware>();
                    app.UseRateLimiter();
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

    #region Helpers

    private const string ApiKeyPath = "/api/v1/app/machine/apikeys/validate";
    private const string WebhookKeyPath = "/api/v1/app/machine/webhookkeys/validate";
    private const string IntrospectPath = "/api/v1/app/machine/tokens/introspect";

    private string MachineToken(string code = AppCode, params string[] scopes) =>
        _tokens.Service.GenerateMachineAccessToken(code, scopes.Length == 0 ? MachineClients.Scopes.All : scopes);

    private static HttpContent BodyFor(string path, string credential) => path switch
    {
        ApiKeyPath => JsonContent.Create(new { apiKey = credential }),
        WebhookKeyPath => JsonContent.Create(new { webhookKey = credential }),
        _ => new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = credential }),
    };

    private async Task<HttpResponseMessage> PostAsync(string path, string? token, string credential = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = BodyFor(path, credential) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string WwwAuthenticate(HttpResponseMessage response) =>
        string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

    private static IReadOnlyList<string> Headers(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not "Date" and not "Content-Length")
            .Select(h => $"{h.Key.ToLowerInvariant()}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response)
    {
        var body = await JsonAsync(response);
        return JsonSerializer.Serialize(body.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.ToString()));
    }

    #endregion

    public static TheoryData<string, string> Checks => new()
    {
        { ApiKeyPath, MachineClients.Scopes.ApiKeysValidate },
        { WebhookKeyPath, MachineClients.Scopes.WebhookKeysValidate },
        { IntrospectPath, MachineClients.Scopes.TokensIntrospect },
    };

    [Fact]
    public void Checks_IsNotEmpty() => Checks.Count<object[]>().Should().Be(3);

    // --- T1: path and scheme ---

    [Fact]
    public async Task ApiKey_OwnKey_WithTheMachineToken_Returns200_WithCodeAndAddresses_NoStore()
    {
        _apiKey = TestHelpers.CreateApiKey(applicationId: _application.Id, keySelector: Selector, keyHash: "stored", allowedIps: ["203.0.113.0/24"]);

        var response = await PostAsync(ApiKeyPath, MachineToken(), _rawKey);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(response);
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "active", "apiKeyId", "applicationId", "applicationCode", "name", "environment", "scopes", "allowedIps");
        body.GetProperty("applicationCode").GetString().Should().Be(AppCode);
        body.GetProperty("allowedIps").EnumerateArray().Select(e => e.GetString()).Should().Equal("203.0.113.0/24");
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task NoToken_Returns401_NoStore(string path, string scope)
    {
        _ = scope;
        var response = await PostAsync(path, token: null, "anything");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _applications.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task APlatformToken_Returns401(string path, string scope)
    {
        _ = scope;
        var response = await PostAsync(path, _tokens.ForPlatform(_user), "anything");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _applications.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task AUsersApplicationToken_Returns401_EvenNamingTheMachineScopes(string path, string scope)
    {
        var response = await PostAsync(path, _tokens.ForApplication(_user, $"openid {scope}", audience: AppCode), "anything");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Unauthenticated);
        _applications.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public async Task AMachineTokenWithoutTheChecksScope_Returns403_NamingIt(string path, string scope)
    {
        var others = MachineClients.Scopes.All.Where(s => s != scope).ToArray();

        var response = await PostAsync(path, MachineToken(AppCode, others), "anything");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.Forbidden);
        WwwAuthenticate(response).Should().Be($"Bearer error=\"insufficient_scope\", scope=\"{scope}\"");
        _apiKeys.VerifyNoOtherCalls();
        _webhookKeys.VerifyNoOtherCalls();
        _refreshTokens.VerifyNoOtherCalls();
    }

    public static TheoryData<string> UnservableApplications => new() { "inactive application", "deleted application" };

    [Fact]
    public void UnservableApplications_IsNotEmpty() => UnservableApplications.Count<object[]>().Should().Be(2);

    [Theory]
    [MemberData(nameof(UnservableApplications))]
    public async Task AnUnservableApplication_Returns401_IdenticalToARevokedToken(string reason)
    {
        _applications.Setup(r => r.GetByCodeAsync(AppCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reason == "inactive application"
                ? TestHelpers.CreateApplication(id: _application.Id, code: AppCode, isActive: false)
                : null);

        var revokedToken = MachineToken();
        _blacklist.Setup(b => b.IsTokenBlacklisted(_tokens.Service.GetTokenId(revokedToken)!)).Returns(true);

        var unservable = await PostAsync(ApiKeyPath, MachineToken(), _rawKey);
        var revoked = await PostAsync(ApiKeyPath, revokedToken, _rawKey);

        unservable.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(unservable)).Should().Be(ChallengeReasonCodes.TokenRevoked);
        Headers(unservable).Should().Equal(Headers(revoked), "one 401 is written in MVC, the other before it");
        (await BodyWithoutTraceAsync(unservable)).Should().Be(await BodyWithoutTraceAsync(revoked));
        _apiKeys.VerifyNoOtherCalls();
    }

    public static TheoryData<string> OldPaths => new()
    {
        "/api/v1/apikeys/validate",
        "/api/v1/webhookkeys/validate",
        "/api/v1/auth/introspect",
    };

    [Fact]
    public void OldPaths_IsNotEmpty() => OldPaths.Count<object[]>().Should().Be(3);

    [Theory]
    [MemberData(nameof(OldPaths))]
    public async Task TheOldUserTokenDoors_AnswerAsAnUnknownRoute(string path)
    {
        // A platform token that would have opened them (D-128-2 (a)).
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = path.EndsWith("introspect", StringComparison.Ordinal)
                ? new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "x" })
                : JsonContent.Create(new { apiKey = _rawKey, webhookKey = "wk_x" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.ForPlatform(_user));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        _apiKeys.VerifyNoOtherCalls();
        _webhookKeys.VerifyNoOtherCalls();
    }

    // --- T2: binding ---

    [Fact]
    public async Task AnotherApplicationsApiKey_IsByteIdenticalToAnUnknownKey_NoHash_NoUse()
    {
        var unknown = ApiKeyFormat.Compose("production", "Unkn0wn000000000", new string('s', 32));

        // Application OTHER's server presents SHOP's key, then a key that exists nowhere.
        var foreign = await PostAsync(ApiKeyPath, MachineToken(OtherCode), _rawKey);
        var missing = await PostAsync(ApiKeyPath, MachineToken(OtherCode), unknown);

        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(foreign)).Should().Be("ApiKey.Invalid");
        Headers(foreign).Should().Equal(Headers(missing));
        (await BodyWithoutTraceAsync(foreign)).Should().Be(await BodyWithoutTraceAsync(missing));

        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _apiKeys.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnotherApplicationsWebhookKey_IsByteIdenticalToAnUnknownKey_NoUse_NoRefingerprint()
    {
        var key = TestHelpers.CreateWebhookKey(applicationId: _application.Id, keyHash: "legacy-form");
        _credentialHasher.Map("wk_prod_known", "issuing-form");
        _webhookKeys
            .Setup(r => r.GetByHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialHashCandidates candidates, CancellationToken _) =>
                candidates.ValuesFor(Auth.Domain.Enums.CredentialPurpose.WebhookKey).Contains("issuing-form") ? key : null);

        var foreign = await PostAsync(WebhookKeyPath, MachineToken(OtherCode), "wk_prod_known");
        var missing = await PostAsync(WebhookKeyPath, MachineToken(OtherCode), "wk_prod_unknown");

        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(foreign)).Should().Be("WebhookKey.Invalid");
        Headers(foreign).Should().Equal(Headers(missing));
        (await BodyWithoutTraceAsync(foreign)).Should().Be(await BodyWithoutTraceAsync(missing));
        // No re-fingerprint either: ValidateWebhookKeyQueryHandlerTests proves the event is not published.
        _webhookKeys.Verify(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // The owner's own server is answered.
        var own = await PostAsync(WebhookKeyPath, MachineToken(AppCode), "wk_prod_known");
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(own)).GetProperty("applicationCode").GetString().Should().Be(AppCode);
    }

    // --- T3 at the wire: introspection, form-encoded ---

    [Fact]
    public async Task Introspect_OwnToken_IsActive_AnotherApplicationsIsInactive_NoStore()
    {
        var own = await PostAsync(IntrospectPath, MachineToken(), _tokens.ForApplication(_user, "openid", audience: AppCode));
        var foreign = await PostAsync(IntrospectPath, MachineToken(), _tokens.ForApplication(_user, "openid", audience: OtherCode));

        own.StatusCode.Should().Be(HttpStatusCode.OK);
        own.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await JsonAsync(own)).GetProperty("active").GetBoolean().Should().BeTrue();

        foreign.StatusCode.Should().Be(HttpStatusCode.OK);
        var inactive = await JsonAsync(foreign);
        inactive.EnumerateObject().Select(p => p.Name).Should().Equal("active");
        inactive.GetProperty("active").GetBoolean().Should().BeFalse();
    }

    private sealed class TheseControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(MachineCredentialChecksController).GetTypeInfo());
            feature.Controllers.Add(typeof(ApiKeysController).GetTypeInfo());
            feature.Controllers.Add(typeof(WebhookKeysController).GetTypeInfo());
            feature.Controllers.Add(typeof(AuthController).GetTypeInfo());
        }
    }
}
