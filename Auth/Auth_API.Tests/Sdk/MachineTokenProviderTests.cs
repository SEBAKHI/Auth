using System.Net;
using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ClientCredentials;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth.Sdk.TokenManagement;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// The SDK's machine client (OI-128, T7). The round-trip tests post the SDK's real assertion to the
/// server's real client_credentials handler, in process, through the SDK's own named client; the
/// others answer with a canned token so time and concurrency can be controlled.
/// </summary>
public sealed class MachineTokenProviderTests : IDisposable
{
    private const string BaseUrl = "https://auth.example.com";
    private const string Issuer = "https://auth.example.com";
    private const string ClientId = "shop";
    private const string GatewayToken = "gateway-token-value";

    private readonly GeneratedMachineKey _key = new MachineKeyMaterial().Generate();
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly Transport _transport = new();
    private ServiceProvider? _provider;

    public void Dispose() => _provider?.Dispose();

    /// <summary>A clock the test moves.</summary>
    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Records every request and answers it with <see cref="Answer"/>.</summary>
    private sealed class Transport : HttpMessageHandler
    {
        private int _count;

        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        public int Count => Volatile.Read(ref _count);

        public Func<HttpRequestMessage, string, Task<HttpResponseMessage>> Answer { get; set; } =
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, new { access_token = "machine-token", token_type = "Bearer", expires_in = 300, scope = "apikeys.validate" }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add((request, body));
            }

            return await Answer(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private ServiceProvider Build(Action<AuthSystemOptions>? customize = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddSingleton<TimeProvider>(_time);
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = BaseUrl;
            options.Issuer = Issuer;
            options.Audience = "platform";
            options.GatewayToken = GatewayToken;
            options.ClientId = ClientId;
            options.MachineKeyId = _key.PublicKey.Kid;
            options.MachinePrivateKeyPem = _key.PrivateKeyPem;
            customize?.Invoke(options);
        });

        // Replace the transport only, for both named clients; the registrations under test stay.
        services.AddHttpClient(AuthSystemConstants.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _transport);
        services.AddHttpClient(AuthSystemConstants.MachineHttpClientName).ConfigurePrimaryHttpMessageHandler(() => _transport);

        _provider = services.BuildServiceProvider();
        return _provider;
    }

    private static Dictionary<string, string> Form(string body) =>
        body.Split('&').Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

    // --- the round trip through the server's own handler ---

    /// <summary>
    /// Routes the token request to the real <see cref="ClientCredentialsCommandHandler"/>, with the
    /// real verifier and token service and a key store that knows the test key.
    /// </summary>
    private JwtSettings ServerAnswersWithItsRealHandler(JwtTokenService tokens)
    {
        var settings = new JwtSettings { Issuer = Issuer, Audience = "platform", KeyId = "server-key" };
        var keys = new Mock<IApplicationMachineKeyRepository>();
        keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => kid == _key.PublicKey.Kid
                ? new MachineKeyCredential(
                    new ApplicationMachineKey(Guid.NewGuid(), Guid.NewGuid(), kid, null, MachineClients.SigningAlgorithm,
                        _key.PublicKey.PublicKeyJwk, DateTime.UtcNow, Guid.NewGuid(), null, null, null, null),
                    ClientId)
                : null);
        var used = new HashSet<string>();
        var jtis = new Mock<IClientAssertionJtiStore>();
        jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string jti, DateTime _, CancellationToken _) => used.Add(jti));

        var handler = new ClientCredentialsCommandHandler(
            new Auth.Application.Features.Authentication.Common.ClientAuthenticator(keys.Object, jtis.Object, new ClientAssertionVerifier(TimeProvider.System),
                TestHelpers.CreateOptions(settings), NullLogger<Auth.Application.Features.Authentication.Common.ClientAuthenticator>.Instance),
            keys.Object, tokens, NullLogger<ClientCredentialsCommandHandler>.Instance);

        _transport.Answer = async (request, body) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/token");
            var form = Form(body);
            form["grant_type"].Should().Be("client_credentials");
            var result = await handler.Handle(
                new ClientCredentialsCommand(
                    form.GetValueOrDefault("client_assertion_type"), form.GetValueOrDefault("client_assertion"),
                    form.GetValueOrDefault("client_id"), form.GetValueOrDefault("scope"), "203.0.113.7"),
                CancellationToken.None);

            return result.IsError
                ? Json(HttpStatusCode.BadRequest, new { code = result.FirstError.Code })
                : Json(HttpStatusCode.OK, result.Value);
        };

        return settings;
    }

    [Fact]
    public async Task RoundTrip_TheServersOwnHandlerAcceptsTheSdkAssertion_AndTheTokenIsAMachineToken()
    {
        var settings = new JwtSettings { Issuer = Issuer, Audience = "platform", KeyId = "server-key" };
        using var tokens = new JwtTokenService(Options.Create(settings), Mock.Of<IPasswordHasher>());
        ServerAnswersWithItsRealHandler(tokens);
        var client = Build().GetRequiredService<AuthSystemClient>();

        var token = await client.GetMachineAccessTokenAsync();

        var validation = await new JsonWebTokenHandler { MapInboundClaims = false }
            .ValidateTokenAsync(token, AccessTokenValidation.Machine(settings, tokens.GetSecurityKey()));
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);
        new JsonWebToken(token).GetPayloadValue<string>("client_id").Should().Be(ClientId);
    }

    /// <summary>The assertion of each token request sent so far, as the server receives it.</summary>
    private List<string> SentAssertions() =>
        _transport.Requests.Select(r => Form(r.Body)["client_assertion"]).ToList();

    [Fact]
    public async Task TheAssertion_HasExactlyTheRfc7523bisShape()
    {
        await Build().GetRequiredService<IMachineTokenProvider>().GetAccessTokenAsync();
        var sent = SentAssertions().Single();

        var assertion = new JsonWebToken(sent);

        assertion.Alg.Should().Be("ES256");
        assertion.Kid.Should().Be(_key.PublicKey.Kid);
        assertion.Typ.Should().Be("client-authentication+jwt");
        assertion.Issuer.Should().Be(ClientId);
        assertion.Subject.Should().Be(ClientId);
        assertion.Audiences.Should().Equal(Issuer);
        assertion.Id.Should().NotBeNullOrWhiteSpace();
        (assertion.ValidTo - assertion.IssuedAt).Should().Be(TimeSpan.FromSeconds(60));
        assertion.TryGetPayloadValue<object>("nbf", out _).Should().BeFalse();

        // And the server's verifier, alone, accepts it.
        var verification = await new ClientAssertionVerifier(TimeProvider.System).VerifyAsync(
            sent, _key.PublicKey.PublicKeyJwk, new ClientAssertionRules(ClientId, Issuer, TimeSpan.FromSeconds(60)));
        verification.IsValid.Should().BeTrue(verification.FailureReason);
    }

    [Fact]
    public async Task TwoRequests_NeverShareAnAssertionOrAJti()
    {
        _transport.Answer = (_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, new { code = "Auth.InvalidClient" }));
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();
        await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();

        var sent = SentAssertions();
        sent.Should().HaveCount(2);
        new JsonWebToken(sent[0]).Id.Should().NotBe(new JsonWebToken(sent[1]).Id);
    }

    // --- caching and single flight ---

    [Fact]
    public async Task ManyCalls_MakeOneTokenRequest()
    {
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        for (var i = 0; i < 10; i++)
        {
            (await provider.GetAccessTokenAsync()).Should().Be("machine-token");
        }

        _transport.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneInFlightRequest()
    {
        var release = new TaskCompletionSource();
        _transport.Answer = async (_, _) =>
        {
            await release.Task;
            return Json(HttpStatusCode.OK, new { access_token = "machine-token", token_type = "Bearer", expires_in = 300 });
        };
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        var callers = Enumerable.Range(0, 25).Select(_ => Task.Run(() => provider.GetAccessTokenAsync())).ToArray();
        await Task.Delay(200);
        release.SetResult();
        var tokens = await Task.WhenAll(callers);

        tokens.Should().AllBe("machine-token");
        _transport.Count.Should().Be(1, "the callers queued behind the first request find its token");
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneFailedRequest_Too()
    {
        // An outage must not become one timeout per queued caller, each spending the token
        // endpoint's per-address rate limit.
        var release = new TaskCompletionSource();
        _transport.Answer = async (_, _) =>
        {
            await release.Task;
            return Json(HttpStatusCode.ServiceUnavailable, new { code = "Http.ServiceUnavailable" });
        };
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        // Called directly, not through Task.Run: each call has joined the single flight (or
        // started it) when it returns its task, so all 25 are waiting before the release, on
        // any runner.
        var callers = Enumerable.Range(0, 25).Select(_ => provider.GetAccessTokenAsync()).ToArray();
        callers.Should().OnlyContain(caller => !caller.IsCompleted);
        release.SetResult();

        foreach (var caller in callers)
        {
            await caller.Invoking(c => c).Should().ThrowAsync<MachineTokenException>();
        }

        _transport.Count.Should().Be(1, "the callers waiting on the failed request share its failure");

        // The failure is not cached: the next call asks again.
        _transport.Answer = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, new { access_token = "machine-token", expires_in = 300 }));
        (await provider.GetAccessTokenAsync()).Should().Be("machine-token");
        _transport.Count.Should().Be(2);
    }

    [Fact]
    public async Task OneCallerGivingUp_DoesNotCancelTheRequestTheOthersWaitOn()
    {
        var release = new TaskCompletionSource();
        _transport.Answer = async (_, _) =>
        {
            await release.Task;
            return Json(HttpStatusCode.OK, new { access_token = "machine-token", expires_in = 300 });
        };
        var provider = Build().GetRequiredService<IMachineTokenProvider>();
        using var impatient = new CancellationTokenSource();

        var first = provider.GetAccessTokenAsync(impatient.Token);
        var second = provider.GetAccessTokenAsync();
        await Task.Delay(100);
        await impatient.CancelAsync();
        await first.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();

        release.SetResult();
        (await second).Should().Be("machine-token");
        _transport.Count.Should().Be(1);
    }

    [Fact]
    public async Task TheToken_IsReplacedOneMinuteBeforeItExpires()
    {
        var provider = Build().GetRequiredService<IMachineTokenProvider>();
        var start = _time.Now;

        await provider.GetAccessTokenAsync();
        _time.Now = start.AddSeconds(239);
        await provider.GetAccessTokenAsync();
        _transport.Count.Should().Be(1, "300 s token, 60 s margin: still good at 239 s");

        _time.Now = start.AddSeconds(241);
        await provider.GetAccessTokenAsync();
        _transport.Count.Should().Be(2, "past 240 s a new token is requested");
    }

    // --- what goes over the wire ---

    [Fact]
    public async Task TheTokenRequest_CarriesNoUserBearer_AndTheGatewayTokenOnce()
    {
        var services = Build();
        var client = services.GetRequiredService<AuthSystemClient>();
        // A user token in the store: the main client attaches it to everything it sends.
        await client.SetTokensAsync("user-access-token", "user-refresh-token", 3600);

        await client.GetMachineAccessTokenAsync();

        var (request, body) = _transport.Requests.Single();
        request.Headers.Authorization.Should().BeNull("the machine client authenticates with its assertion alone");
        request.Headers.GetValues(AuthSystemConstants.GatewayTokenHeaderName).Should().Equal(GatewayToken);
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("https://auth.example.com/api/v1/auth/token");

        var form = Form(body);
        form.Keys.Should().BeEquivalentTo("grant_type", "client_assertion_type", "client_assertion", "client_id");
        form["client_assertion_type"].Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        form["client_id"].Should().Be(ClientId);
        body.Should().NotContain("user-access-token").And.NotContain("PRIVATE");
    }

    // --- failures ---

    [Fact]
    public async Task ARefusal_IsThrownAsNotTransient_AndIsNotCached()
    {
        _transport.Answer = (_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, new { code = "Auth.InvalidClient" }));
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        var first = await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();
        first.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        first.Which.ErrorCode.Should().Be("Auth.InvalidClient");
        first.Which.IsTransient.Should().BeFalse();

        await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();
        _transport.Count.Should().Be(2, "a failure is never cached; each call asks again with a new assertion");
    }

    [Fact]
    public async Task AnOutage_IsThrownAsTransient_WithItsRetryAfter()
    {
        _transport.Answer = (_, _) =>
        {
            var response = Json(HttpStatusCode.ServiceUnavailable, new { code = "Http.ServiceUnavailable" });
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        };
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        var thrown = await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();
        thrown.Which.IsTransient.Should().BeTrue();
        thrown.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task NoAnswer_IsThrownAsTransient()
    {
        _transport.Answer = (_, _) => throw new HttpRequestException("connection refused");
        var provider = Build().GetRequiredService<IMachineTokenProvider>();

        var thrown = await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<MachineTokenException>();
        thrown.Which.StatusCode.Should().BeNull();
        thrown.Which.IsTransient.Should().BeTrue();
    }

    public static TheoryData<string> MachineOptions => new() { "ClientId", "MachineKeyId", "MachinePrivateKeyPem", "Issuer" };

    [Theory]
    [MemberData(nameof(MachineOptions))]
    public async Task AnUnconfiguredMachineClient_SaysSo_AndSendsNothing(string missing)
    {
        // Issuer included: without it the assertion has no audience, and the refusal would read as
        // a wrong key instead of the configuration error it is.
        var provider = Build(options => typeof(AuthSystemOptions).GetProperty(missing)!.SetValue(options, ""))
            .GetRequiredService<IMachineTokenProvider>();

        await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<InvalidOperationException>();
        _transport.Count.Should().Be(0);
    }

    [Fact]
    public async Task APrivateKeyOnAnotherCurve_IsAConfigurationError()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var provider = Build(options => options.MachinePrivateKeyPem = p384.ExportPkcs8PrivateKeyPem())
            .GetRequiredService<IMachineTokenProvider>();

        await provider.Invoking(p => p.GetAccessTokenAsync()).Should().ThrowAsync<InvalidOperationException>();
        _transport.Count.Should().Be(0);
    }

    [Fact]
    public void TheMachineClient_HasNoTokenRefreshHandler()
    {
        // The registration itself: the main client carries the refresh handler, the machine one not.
        var factory = Build().GetRequiredService<IHttpMessageHandlerFactory>();

        Chain(factory.CreateHandler(AuthSystemConstants.HttpClientName)).Should().Contain(h => h is TokenRefreshHandler);
        Chain(factory.CreateHandler(AuthSystemConstants.MachineHttpClientName)).Should().NotContain(h => h is TokenRefreshHandler);
    }

    [Fact]
    public void TheMachineClient_DoesNotFollowRedirects()
    {
        // The SDK's own registration, transport not replaced: a 307 or 308 followed would re-post
        // the assertion and the gateway token to the redirect's target.
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = BaseUrl;
            options.Issuer = Issuer;
            options.Audience = "platform";
        });
        using var provider = services.BuildServiceProvider();

        var innermost = Chain(provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(AuthSystemConstants.MachineHttpClientName)).Last();

        var followsRedirects = innermost switch
        {
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            HttpClientHandler client => client.AllowAutoRedirect,
            _ => throw new InvalidOperationException($"Unexpected primary handler {innermost.GetType()}")
        };
        followsRedirects.Should().BeFalse();
    }

    private static List<HttpMessageHandler> Chain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }
}
