using System.Net;
using Auth.Application.Configuration;
using Auth.Infrastructure.BotDefense;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS4 (BD4, IP10): one deadline over both attempts; one second attempt after a transport
/// failure or a 5xx, with the same idempotency key; no second attempt after a 429 or a 4xx
/// without codes; running out of time is final; an outage is a result, never a 500; the breaker
/// opens after five calls without an answer and lets one probe through after 30 seconds. Only the
/// secret, the token and the idempotency key are sent, never the visitor's address.
/// </summary>
/// <remarks>
/// A stub handler stands in for Cloudflare: CI never calls it. Deliberate breaks, each verified
/// red: the timeout filter without its <c>when</c> (the caller's own cancellation reads as a
/// timeout); a breaker that never opens.
/// </remarks>
public class TurnstileSiteverifyClientTests
{
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
    private readonly StubCloudflare _cloudflare = new();
    private readonly BotDefenseCircuitBreaker _breaker;

    public TurnstileSiteverifyClientTests()
    {
        _breaker = new BotDefenseCircuitBreaker(_clock, NullLogger<BotDefenseCircuitBreaker>.Instance);
    }

    private TurnstileSiteverifyClient Client(int timeoutMs = 2000) =>
        new(
            new HttpClient(_cloudflare),
            TestHelpers.CreateOptions(new BotDefenseSettings { SiteverifyTimeoutMs = timeoutMs }),
            _breaker,
            _clock,
            NullLogger<TurnstileSiteverifyClient>.Instance);

    private const string Success =
        """{"success":true,"challenge_ts":"2026-11-12T09:59:55.000Z","hostname":"accounts.example.com","error-codes":[],"action":"login"}""";

    [Fact]
    public async Task SendsTheSecretTheTokenAndAKey_AndNothingElse()
    {
        _cloudflare.Then(HttpStatusCode.OK, Success);

        var result = await Client().VerifyAsync("the-secret", "the-token", CancellationToken.None);

        result.Answer!.Success.Should().BeTrue();
        result.Answer.Hostname.Should().Be("accounts.example.com");
        result.Answer.Action.Should().Be("login");
        var sent = _cloudflare.Requests.Single();
        sent.Uri.Should().Be(TurnstileSiteverifyClient.Endpoint);
        sent.Form.Keys.Should().BeEquivalentTo(["secret", "response", "idempotency_key"],
            "never remoteip (IP10): Cloudflare learns nothing about the visitor from the server");
        sent.Form["secret"].Should().Be("the-secret");
        sent.Form["response"].Should().Be("the-token");
        Guid.TryParse(sent.Form["idempotency_key"], out _).Should().BeTrue();
    }

    [Fact]
    public async Task TheAnswerCarriesCloudflaresOwnTime_FromItsDateHeader()
    {
        var answeredAt = new DateTimeOffset(2026, 11, 12, 10, 0, 1, TimeSpan.Zero);
        _cloudflare.ThenAnsweredAt(answeredAt, Success);

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer!.RespondedAt.Should().Be(answeredAt, "the challenge's age is measured on Cloudflare's clock");
        result.Answer.ChallengeTs.Should().Be(new DateTimeOffset(2026, 11, 12, 9, 59, 55, TimeSpan.Zero));
    }

    [Fact]
    public async Task A5xx_IsTriedOnceMore_WithTheSameKey()
    {
        _cloudflare.Then(HttpStatusCode.ServiceUnavailable, "").Then(HttpStatusCode.OK, Success);

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().NotBeNull();
        result.Attempts.Should().Be(2);
        _cloudflare.Requests.Select(r => r.Form["idempotency_key"]).Distinct().Should().ContainSingle(
            "Cloudflare answers a repeated key as it answered the first, so the token is never spent twice");
    }

    [Fact]
    public async Task ATransportFailure_IsTriedOnceMore()
    {
        _cloudflare.ThenThrow().Then(HttpStatusCode.OK, Success);

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().NotBeNull();
        result.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Two5xx_AreUnavailable_NeverAThrow()
    {
        _cloudflare.Then(HttpStatusCode.BadGateway, "").Then(HttpStatusCode.BadGateway, "");

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().BeNull();
        result.Failure.Should().Be("http-502");
        result.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task A429_IsUnavailable_WithoutASecondAttempt()
    {
        _cloudflare.Then(HttpStatusCode.TooManyRequests, "");

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().BeNull();
        result.Failure.Should().Be("http-429");
        _cloudflare.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A4xxWithoutCodes_IsUnavailable_WithoutASecondAttempt()
    {
        _cloudflare.Then(HttpStatusCode.BadRequest, "not json");

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().BeNull();
        _cloudflare.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A4xxNamingItsCodes_IsAnAnswer()
    {
        _cloudflare.Then(HttpStatusCode.BadRequest, """{"success":false,"error-codes":["bad-request"]}""");

        var result = await Client().VerifyAsync("s", "t", CancellationToken.None);

        result.Answer!.ErrorCodes.Should().Equal("bad-request");
    }

    [Fact]
    public async Task RunningOutOfTime_IsFinal()
    {
        _cloudflare.ThenHang().Then(HttpStatusCode.OK, Success);

        var result = await Client(timeoutMs: 500).VerifyAsync("s", "t", CancellationToken.None);

        result.Answer.Should().BeNull();
        result.Failure.Should().Be("timeout");
        _cloudflare.Requests.Should().ContainSingle("the deadline covers both attempts, and its end is final");
    }

    [Fact]
    public async Task TheCallersOwnCancellation_Propagates()
    {
        _cloudflare.ThenHang();
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var call = () => Client(timeoutMs: 5000).VerifyAsync("s", "t", caller.Token);

        await call.Should().ThrowAsync<OperationCanceledException>(
            "a request the client abandoned is not an outage, and is not counted as one");
    }

    [Fact]
    public async Task FiveCallsWithoutAnAnswer_OpenTheBreaker_ForThirtySeconds()
    {
        for (var i = 0; i < 5; i++)
        {
            _cloudflare.Then(HttpStatusCode.TooManyRequests, "");
            (await Client().VerifyAsync("s", "t", CancellationToken.None)).Answer.Should().BeNull();
        }

        var whileOpen = await Client().VerifyAsync("s", "t", CancellationToken.None);
        whileOpen.Failure.Should().Be("breaker-open");
        whileOpen.Attempts.Should().Be(0);
        _cloudflare.Requests.Should().HaveCount(5, "nothing is called while the breaker is open");

        _clock.Advance(BotDefenseCircuitBreaker.OpenDuration + TimeSpan.FromSeconds(1));
        _cloudflare.Then(HttpStatusCode.OK, Success);
        var probe = await Client().VerifyAsync("s", "t", CancellationToken.None);
        probe.Answer.Should().NotBeNull("one call goes through after the open period");
        _breaker.IsOpen.Should().BeFalse("an answer closes it");
    }

    [Fact]
    public async Task ARefusedToken_IsAnAnswer_AndNeverOpensTheBreaker()
    {
        for (var i = 0; i < 7; i++)
        {
            _cloudflare.Then(HttpStatusCode.OK, """{"success":false,"error-codes":["invalid-input-response"]}""");
            await Client().VerifyAsync("s", "junk", CancellationToken.None);
        }

        _breaker.IsOpen.Should().BeFalse("a stranger sending junk tokens cannot open the breaker");
        _cloudflare.Requests.Should().HaveCount(7);
    }

    /// <summary>Cloudflare's stand-in: answers each request with the next queued reply.</summary>
    private sealed class StubCloudflare : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _replies = new();

        public List<(Uri Uri, Dictionary<string, string> Form)> Requests { get; } = [];

        public StubCloudflare Then(HttpStatusCode status, string body)
        {
            _replies.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
            return this;
        }

        public StubCloudflare ThenAnsweredAt(DateTimeOffset date, string body)
        {
            _replies.Enqueue(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
                response.Headers.Date = date;
                return Task.FromResult(response);
            });
            return this;
        }

        public StubCloudflare ThenThrow()
        {
            _replies.Enqueue(_ => throw new HttpRequestException("connection reset"));
            return this;
        }

        public StubCloudflare ThenHang()
        {
            _replies.Enqueue(async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, body.Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')))));
            return await _replies.Dequeue()(cancellationToken);
        }
    }
}
