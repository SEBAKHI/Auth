using System.Net;
using System.Text;
using Auth.Sdk;
using Auth.Sdk.TokenManagement;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// SDK 2.1 (S12, TS8): a stored token the AuthSystem refuses before it expires (its signing key was
/// withdrawn, its session ended) is refreshed once and the request retried with the new token.
/// Until 2.1 the handler refreshed only a token near its expiry, so it retried with the refused
/// token itself and the application stayed refused until that token expired.
/// </summary>
/// <remarks>
/// The protected API and the refresh endpoint are scripted transports. Every token here is an hour
/// from expiry, far outside <see cref="AuthSystemOptions.RefreshBufferSeconds"/>.
/// </remarks>
public sealed class TokenRefreshHandlerForcedRefreshTests : IDisposable
{
    private readonly ScriptedApi _api = new();
    private readonly ScriptedRefresh _refresh = new();
    private readonly InMemoryTokenStore _store = new();
    private readonly HttpClient _client;

    public TokenRefreshHandlerForcedRefreshTests()
    {
        _store.SetAsync("access-0", "refresh-0", 3600).GetAwaiter().GetResult();
        var handler = new TokenRefreshHandler(
            _store,
            Options.Create(new AuthSystemOptions { BaseUrl = "https://auth.example.com", RefreshBufferSeconds = 120 }),
            NullLogger<TokenRefreshHandler>.Instance,
            new ForcedRefreshGuard(),
            () => new HttpClient(_refresh, disposeHandler: false) { BaseAddress = new Uri("https://auth.example.com") })
        {
            InnerHandler = _api
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri("https://app.example.com") };
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ARefusedTokenThatHasNotExpired_IsRefreshedOnce_AndTheRequestRetriedWithTheNewOne()
    {
        _api.Refuse("access-0");

        var response = await _client.GetAsync("/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _refresh.Calls.Should().Be(1);
        _refresh.Presented.Should().Equal("refresh-0");
        _api.Seen.Should().Equal("access-0", "access-1");
        (await _store.GetAsync())!.AccessToken.Should().Be("access-1");
    }

    [Fact]
    public async Task ConcurrentRequestsRefusedOnTheSameToken_ShareOneRefresh()
    {
        _api.Refuse("access-0");
        _api.HoldUntil(requests: 2);

        var responses = await Task.WhenAll(_client.GetAsync("/orders"), _client.GetAsync("/invoices"));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        _refresh.Calls.Should().Be(1, "the second request finds the token already replaced and uses the replacement");
        _api.Seen.Count(token => token == "access-1").Should().Be(2);
    }

    [Fact]
    public async Task ATokenAForcedRefreshProduced_IsNotForcedAgain_UntilARequestWithItSucceeds()
    {
        _api.Refuse("access-0", "access-1");

        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _refresh.Calls.Should().Be(1, "the replacement was refused too: refreshing again would cost a refresh per request");

        // A request with it succeeds: from then on a refusal of it is worth one refresh again.
        _api.Refuse();
        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
        _api.Refuse("access-1");
        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.OK);

        _refresh.Calls.Should().Be(2);
        (await _store.GetAsync())!.AccessToken.Should().Be("access-2");
    }

    [Fact]
    public async Task ARefreshTheAuthSystemRefuses_LeavesTheRefusalAsItIs()
    {
        _api.Refuse("access-0");
        _refresh.RefuseNext = true;

        var response = await _client.GetAsync("/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _api.Seen.Should().Equal("access-0");
        (await _store.GetAsync()).Should().BeNull("a refused refresh token ends the stored session, as before");
    }

    [Fact]
    public async Task ARefusedTokenWhoseRefreshFailed_IsNotRefreshedAgainOnTheNextRefusal()
    {
        _api.Refuse("access-0");
        _refresh.FailWith = HttpStatusCode.ServiceUnavailable;

        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _refresh.Calls.Should().Be(1, "one forced refresh per refused value: a busy AuthSystem is not asked again per request");
        (await _store.GetAsync())!.AccessToken.Should().Be("access-0", "the session is kept for the refresh at expiry");
    }

    /// <summary>The protected API: refuses the listed tokens, answers 200 to any other.</summary>
    private sealed class ScriptedApi : HttpMessageHandler
    {
        private readonly object _gate = new();
        private HashSet<string> _refused = [];
        private TaskCompletionSource? _hold;
        private int _holdCount;

        public List<string> Seen { get; } = [];

        public void Refuse(params string[] tokens)
        {
            lock (_gate)
            {
                _refused = [.. tokens];
            }
        }

        /// <summary>Holds the first answers until <paramref name="requests"/> have arrived, so they overlap.</summary>
        public void HoldUntil(int requests)
        {
            _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdCount = requests;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization?.Parameter ?? "";
            bool refused;
            TaskCompletionSource? hold;
            lock (_gate)
            {
                Seen.Add(token);
                refused = _refused.Contains(token);
                hold = _hold;
                if (hold is not null && Seen.Count >= _holdCount)
                {
                    _hold = null;
                    hold.SetResult();
                }
            }

            if (hold is not null)
            {
                await hold.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return new HttpResponseMessage(refused ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        }
    }

    /// <summary>The refresh endpoint: rotates <c>refresh-n</c> into <c>access-(n+1)</c>, <c>refresh-(n+1)</c>.</summary>
    private sealed class ScriptedRefresh : HttpMessageHandler
    {
        private int _issued;

        public int Calls => Presented.Count;

        public List<string> Presented { get; } = [];

        public bool RefuseNext { get; set; }

        /// <summary>When set, every refresh fails with this status and stores nothing.</summary>
        public HttpStatusCode? FailWith { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/refresh");
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var presented = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("refreshToken").GetString()!;
            lock (Presented)
            {
                Presented.Add(presented);
            }

            if (RefuseNext)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            if (FailWith is { } status)
            {
                return new HttpResponseMessage(status);
            }

            var n = Interlocked.Increment(ref _issued);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"accessToken":"access-{{n}}","refreshToken":"refresh-{{n}}","expiresIn":3600}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
