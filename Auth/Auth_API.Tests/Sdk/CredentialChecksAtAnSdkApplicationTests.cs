using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Auth.Domain.Services;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth.Sdk.TokenManagement;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// SDK 2.0's key schemes in an application that uses them (OI-128 PR B, B3, B6, B8, B9): a real
/// host with <c>AddAuthSystemAuthentication().AddAuthSystemApiKey()</c> and an endpoint guarded by
/// exactly the attribute the guide recommends, <c>[Authorize(AuthenticationSchemes = "Bearer,ApiKey")]</c>.
/// The AuthSystem is a scripted transport behind the credential checks' own named client, and the
/// machine token comes from a stub provider, so every answer the SDK can meet is played at it.
/// </summary>
public sealed class CredentialChecksAtAnSdkApplicationTests : IAsyncLifetime
{
    private const string ClientId = "crm-web";
    private const string Endpoint = "/orders";
    private const string WebhookEndpoint = "/hooks";

    private readonly ScriptedAuthSystem _authSystem = new();
    private readonly StubMachineTokens _machineTokens = new();
    private readonly AdjustableTimeProvider _time = new();
    private readonly string _key = Key(1);
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
                    services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));

                    // A host cache with a size limit: an SDK that wrote into it without a size
                    // would throw on every entry.
                    services.AddMemoryCache(options => options.SizeLimit = 1);

                    services.AddAuthSystemAuthentication(Configure)
                        .AddAuthSystemApiKey()
                        .AddAuthSystemWebhookKey();
                    services.AddHttpClient(AuthSystemConstants.MachineApiHttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => _authSystem);
                    services.AddSingleton<IMachineTokenProvider>(_machineTokens);
                    services.AddSingleton<TimeProvider>(_time);
                    services.AddAuthorization();
                })
                .Configure(app =>
                {
                    // The caller's address, as the connection would carry it.
                    app.Use((context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Remote", out var remote))
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Parse(remote.ToString());
                        }

                        return next(context);
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(Endpoint, () => Results.Ok("orders"))
                            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Bearer,ApiKey" });
                        endpoints.MapPost(WebhookEndpoint, () => Results.Ok("hook"))
                            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Bearer,WebhookKey" });
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

    private static void Configure(AuthSystemOptions options)
    {
        options.BaseUrl = "https://auth.example.com";
        options.Issuer = "https://auth.example.com";
        options.Audience = ClientId;
        options.ClientId = ClientId;
        options.MachineKeyId = "kid-1";
        options.MachinePrivateKeyPem = "stub: the provider is replaced";
    }

    /// <summary>A well-formed key whose selector carries <paramref name="n"/>.</summary>
    private static string Key(int n) =>
        ApiKeyFormat.Compose("production", n.ToString("D16"), new string('s', 32));

    private async Task<HttpResponseMessage> CallAsync(string? apiKey, string remote = "203.0.113.7")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add("X-Test-Remote", remote);
        if (apiKey is not null)
        {
            request.Headers.Add(AuthSystemConstants.ApiKeyHeaderName, apiKey);
        }

        return await _client.SendAsync(request);
    }

    private static string Valid(string applicationCode = "CRM-WEB", string allowedIps = "[]") =>
        $$"""{"active":true,"apiKeyId":"{{Guid.NewGuid()}}","applicationId":"{{Guid.NewGuid()}}","applicationCode":"{{applicationCode}}","name":"Key","environment":"production","scopes":["crm:orders:read"],"allowedIps":{{allowedIps}}}""";

    private const string InvalidProblem = """{"type":"about:blank","status":400,"code":"ApiKey.Invalid","detail":"The provided API key is invalid."}""";

    // --- valid, and the machine token ---

    [Fact]
    public async Task AValidKey_IsAccepted_AskedWithTheMachineToken_NeverAStoredUserToken()
    {
        await _host.Services.GetRequiredService<AuthSystemClient>().SetTokensAsync("user-access-token", "user-refresh-token", 3600);
        _authSystem.Answer(HttpStatusCode.OK, Valid());

        var response = await CallAsync(_key);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = _authSystem.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().Be(AuthSystemConstants.ApiKeyValidationPath);
        sent.Authorization.Should().Be("Bearer machine-1");
        sent.AllHeaders.Should().NotContain("user-access-token");
        sent.Body.Should().Contain(_key);
    }

    [Fact]
    public async Task AValidKey_IsCached_ForTheConfiguredDuration()
    {
        _authSystem.Answer(HttpStatusCode.OK, Valid());

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.OK);
        _authSystem.Requests.Should().HaveCount(1);

        _time.Advance(TimeSpan.FromSeconds(61));
        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.OK);
        _authSystem.Requests.Should().HaveCount(2, "a valid result is never kept past its time");
    }

    [Fact]
    public async Task AResultNamingAnotherApplication_IsRefused()
    {
        _authSystem.Answer(HttpStatusCode.OK, Valid(applicationCode: "OTHER-APP"));

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheApplicationCode_IsComparedAsTheAuthSystemComparesCodes()
    {
        // ClientId is configured lower case; the AuthSystem stores and answers codes upper case.
        _authSystem.Answer(HttpStatusCode.OK, Valid(applicationCode: "CRM-WEB"));

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- invalid ---

    [Fact]
    public async Task AnInvalidKey_Is401_AndIsAnsweredLocallyFor30Seconds()
    {
        _authSystem.Answer(HttpStatusCode.BadRequest, InvalidProblem);

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _authSystem.Requests.Should().HaveCount(1, "the second refusal came from the negative cache");

        _time.Advance(TimeSpan.FromSeconds(31));
        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _authSystem.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("random-text")]
    [InlineData("ak_prod_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task AMalformedKey_Is401_WithoutAnyCall(string raw)
    {
        (await CallAsync(raw)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(Key(7)[..^1] + (Key(7)[^1] == 'A' ? 'B' : 'A'))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _authSystem.Requests.Should().BeEmpty();
    }

    // --- unavailable: 503 with Retry-After under Bearer,ApiKey, nothing cached ---

    public static TheoryData<string> Transients => new()
    {
        "429", "500", "503", "403 from the AuthSystem", "404 from an older AuthSystem", "network", "timeout",
        "machine token: transient", "machine token: refused", "unreadable body",
    };

    [Fact]
    public void Transients_IsNotEmpty() => Transients.Count<object[]>().Should().Be(10);

    [Theory]
    [MemberData(nameof(Transients))]
    public async Task ACheckThatCannotBeMade_Is503WithRetryAfter_AndCachesNothing(string kind)
    {
        var expectedRetry = "5";
        switch (kind)
        {
            case "429":
                _authSystem.Answer(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 7);
                expectedRetry = "7";
                break;
            case "500": _authSystem.Answer(HttpStatusCode.InternalServerError, "{}"); break;
            case "503": _authSystem.Answer(HttpStatusCode.ServiceUnavailable, "{}"); break;
            case "403 from the AuthSystem": _authSystem.Answer(HttpStatusCode.Forbidden, """{"code":"Http.Forbidden"}"""); break;
            case "404 from an older AuthSystem": _authSystem.Answer(HttpStatusCode.NotFound, ""); break;
            case "network": _authSystem.Fail(new HttpRequestException("connection refused")); break;
            case "timeout": _authSystem.Fail(new TaskCanceledException("the client's timeout")); break;
            case "machine token: transient":
                _machineTokens.Throw = new MachineTokenException("down", null, null, TimeSpan.FromSeconds(9));
                expectedRetry = "9";
                break;
            case "machine token: refused":
                _machineTokens.Throw = new MachineTokenException("refused", HttpStatusCode.BadRequest, "Auth.InvalidClient", null);
                break;
            default: _authSystem.Answer(HttpStatusCode.OK, "not json"); break;
        }

        var first = await CallAsync(_key);
        var second = await CallAsync(_key);

        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "could not tell is not invalid");
        first.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(int.Parse(expectedRetry)));
        first.Headers.WwwAuthenticate.Should().BeEmpty();
        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        if (_machineTokens.Throw is null)
        {
            _authSystem.Requests.Should().HaveCount(2, "nothing was cached: the second request asked again");
        }
    }

    [Fact]
    public async Task ARefusedMachineToken_IsDroppedAndRenewedOnce()
    {
        _authSystem.Answer(HttpStatusCode.Unauthorized, "", then: (HttpStatusCode.OK, Valid()));

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.OK);

        _authSystem.Requests.Select(r => r.Authorization).Should().Equal("Bearer machine-1", "Bearer machine-2");
        _machineTokens.Invalidated.Should().Equal("machine-1");
    }

    [Fact]
    public async Task AMachineTokenRefusedTwice_Is503_NotARetryLoop()
    {
        _authSystem.Answer(HttpStatusCode.Unauthorized, "");

        (await CallAsync(_key)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        _authSystem.Requests.Should().HaveCount(2, "one renewal, no more");
    }

    // --- the address list ---

    [Theory]
    [InlineData("10.20.30.40", HttpStatusCode.OK)]
    [InlineData("::ffff:10.20.30.40", HttpStatusCode.OK)]
    [InlineData("2001:db8::7", HttpStatusCode.OK)]
    [InlineData("192.0.2.1", HttpStatusCode.Unauthorized)]
    [InlineData("2001:db9::7", HttpStatusCode.Unauthorized)]
    public async Task TheAddressList_IsAppliedToTheConnectionsAddress(string remote, HttpStatusCode expected)
    {
        _authSystem.Answer(HttpStatusCode.OK, Valid(allowedIps: """["10.0.0.0/8","2001:db8::/32"]"""));

        var response = await CallAsync(_key, remote);

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.Unauthorized)
        {
            response.Headers.RetryAfter.Should().BeNull("an address outside the list is an invalid key, with no hint");
        }
    }

    // --- the webhook key scheme, the same three outcomes ---

    [Fact]
    public async Task WebhookKey_Valid200_Invalid401_Unavailable503()
    {
        async Task<HttpStatusCode> Hook(string key) =>
            (await _client.PostAsync($"{WebhookEndpoint}?whk={key}", null)).StatusCode;

        _authSystem.Answer(HttpStatusCode.OK, """{"active":true,"webhookKeyId":"00000000-0000-0000-0000-000000000001","applicationId":"00000000-0000-0000-0000-000000000002","applicationCode":"CRM-WEB","name":"Hook","targetUrl":"https://crm.example.com/hook","environment":"production"}""");
        (await Hook("wk_prod_one")).Should().Be(HttpStatusCode.OK);

        _authSystem.Answer(HttpStatusCode.BadRequest, """{"code":"WebhookKey.Invalid"}""");
        (await Hook("wk_prod_two")).Should().Be(HttpStatusCode.Unauthorized);

        _authSystem.Answer(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 3);
        var throttled = await _client.PostAsync($"{WebhookEndpoint}?whk=wk_prod_three", null);
        throttled.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        throttled.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));

        (await Hook("not-a-webhook-key")).Should().Be(HttpStatusCode.Unauthorized);
        _authSystem.Requests.Select(r => r.Path).Should().AllBe(AuthSystemConstants.WebhookKeyValidationPath);
    }

    // --- the caches are the SDK's own, and bounded ---

    [Fact]
    public async Task TheNegativeCache_IsBounded_AndNotTheHostsCache()
    {
        _authSystem.Answer(HttpStatusCode.BadRequest, InvalidProblem);
        var client = _host.Services.GetRequiredService<AuthSystemClient>();

        for (var i = 0; i < AuthSystemClient.CacheSizeLimit + 50; i++)
        {
            await client.ValidateApiKeyAsync(Key(1000 + i));
        }

        var invalid = (MemoryCache)typeof(AuthSystemClient)
            .GetField("_invalidResults", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client)!;
        invalid.Count.Should().BeLessThanOrEqualTo(AuthSystemClient.CacheSizeLimit);
        invalid.Should().NotBeSameAs(_host.Services.GetRequiredService<IMemoryCache>());
    }

    // --- registration: the machine client is required at startup ---

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("WebhookKey")]
    public void AKeyScheme_WithoutTheMachineClient_FailsAtStartup_WithAClearMessage(string scheme)
    {
        var services = new ServiceCollection();
        var builder = services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = "https://auth.example.com";
            options.Issuer = "https://auth.example.com";
            options.Audience = ClientId;
        });

        var register = scheme == "ApiKey" ? () => builder.AddAuthSystemApiKey() : (Func<object>)(() => builder.AddAuthSystemWebhookKey());

        register.Should().Throw<InvalidOperationException>()
            .WithMessage($"The {scheme} scheme validates keys as this application*MachineKeyId*");
    }

    [Fact]
    public void AKeyScheme_WithTheMachineClient_Registers()
    {
        var services = new ServiceCollection();
        var register = () => services.AddAuthSystemAuthentication(Configure).AddAuthSystemApiKey().AddAuthSystemWebhookKey();

        register.Should().NotThrow();
    }

    /// <summary>The AuthSystem's side of the credential checks, played from a script.</summary>
    private sealed class ScriptedAuthSystem : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, int? RetryAfter)> _next = new();
        private (HttpStatusCode Status, string Body, int? RetryAfter) _always = (HttpStatusCode.InternalServerError, "", null);
        private Exception? _failure;

        public List<(string Path, string? Authorization, string AllHeaders, string Body)> Requests { get; } = [];

        public void Answer(HttpStatusCode status, string body, int? retryAfterSeconds = null, (HttpStatusCode Status, string Body)? then = null)
        {
            _failure = null;
            _next.Clear();
            if (then is { } after)
            {
                _next.Enqueue((status, body, retryAfterSeconds));
                _always = (after.Status, after.Body, null);
            }
            else
            {
                _always = (status, body, retryAfterSeconds);
            }
        }

        public void Fail(Exception failure) => _failure = failure;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                string.Join("\n", request.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}")),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_failure is not null)
            {
                throw _failure;
            }

            var (status, body, retryAfter) = _next.Count > 0 ? _next.Dequeue() : _always;
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json"),
            };
            if (retryAfter is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }

    /// <summary>A machine token provider that issues machine-1, machine-2, … one per invalidation.</summary>
    private sealed class StubMachineTokens : IMachineTokenProvider
    {
        private int _generation = 1;

        public Exception? Throw { get; set; }

        public List<string> Invalidated { get; } = [];

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Throw is not null ? Task.FromException<string>(Throw) : Task.FromResult($"machine-{_generation}");

        public void Invalidate(string accessToken)
        {
            Invalidated.Add(accessToken);
            if (accessToken == $"machine-{_generation}")
            {
                _generation++;
            }
        }
    }
}
