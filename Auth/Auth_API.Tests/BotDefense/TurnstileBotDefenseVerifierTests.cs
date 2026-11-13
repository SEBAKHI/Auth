using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Infrastructure.BotDefense;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS3 (BD5, IP10, D-S13-3 (a), ARR-0406, ARR-0412): the verdict table. The configuration is
/// judged before the token; a testing key passes outside Production only; an accepted token must
/// name an accepted host, the surface's action and a recent challenge; the accepted hosts are the
/// first-party apps', and only while that list is empty the CORS origins'.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the sitekey judged before the secret (an empty sitekey
/// with a secret reads Unconfigured); the testing-key rule applied outside Production too; the
/// first-party list ignored in favour of the CORS list.
/// </remarks>
public class TurnstileBotDefenseVerifierTests
{
    private readonly FakeSiteverifyClient _client = new();

    private Task<BotDefenseVerdict> Verify(
        BotDefenseSettings settings,
        string? token = "a-token",
        string action = BotDefenseActions.Login,
        bool production = false,
        IdentityProviderSettings? firstParty = null,
        Microsoft.Extensions.Configuration.IConfiguration? cors = null,
        TimeProvider? clock = null) =>
        BotDefenseTestDoubles.Verifier(settings, _client, production, firstParty, cors, clock)
            .VerifyAsync(token, action, CancellationToken.None);

    [Fact]
    public async Task NeitherKey_IsUnconfigured_WhateverTheRequestCarried()
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(siteKey: "", secret: ""), token: null);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Unconfigured);
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptySitekeyWithASecret_IsMisconfigured_NotUnconfigured()
    {
        // ARR-0406: clearing the sitekey from the console must not switch the check off.
        var verdict = await Verify(BotDefenseTestDoubles.Settings(siteKey: ""), token: null);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Misconfigured);
        verdict.Codes.Should().Contain("sitekey-missing");
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ASitekeyWithoutASecret_IsMisconfigured()
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(secret: ""));

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Misconfigured);
        verdict.Codes.Should().Contain("secret-missing");
    }

    [Theory]
    [InlineData("1x00000000000000000000AA", BotDefenseTestDoubles.Secret)]
    [InlineData("3x00000000000000000000FF", BotDefenseTestDoubles.Secret)]
    [InlineData(BotDefenseTestDoubles.SiteKey, "1x0000000000000000000000000000000AA")]
    public async Task ATestingKeyInProduction_IsMisconfigured_WithoutACall(string siteKey, string secret)
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(siteKey: siteKey, secret: secret), production: true);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Misconfigured);
        verdict.Codes.Should().Contain("testing-key-in-production");
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATestingSecretOutsideProduction_PassesOnCloudflaresSuccessAlone()
    {
        // Its documented answer names no real host or action.
        _client.Answers(BotDefenseTestDoubles.Accepted("test", hostname: "localhost", solvedAt: DateTimeOffset.Parse("2022-02-28T15:14:30Z")));

        var verdict = await Verify(
            BotDefenseTestDoubles.Settings(siteKey: "1x00000000000000000000AA", secret: "1x0000000000000000000000000000000AA"),
            token: TurnstileTestingKeys.DummyToken);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Passed);
    }

    [Fact]
    public async Task NoToken_IsMissing()
    {
        (await Verify(BotDefenseTestDoubles.Settings(), token: null)).Kind.Should().Be(BotDefenseVerdictKind.Missing);
        (await Verify(BotDefenseTestDoubles.Settings(), token: "  ")).Kind.Should().Be(BotDefenseVerdictKind.Missing);
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenLongerThanCloudflareIssues_IsRejected_WithoutACall()
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(), token: new string('t', TurnstileBotDefenseVerifier.MaxTokenLength + 1));

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheDummyTokenInProduction_IsRejected_WithoutACall()
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(), token: TurnstileTestingKeys.DummyToken, production: true);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        verdict.Codes.Should().Contain("dummy-token");
        _client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("tok\u00e9n")]
    [InlineData("a token")]
    [InlineData("tok\u0000en")]
    [InlineData("\u0627\u0644\u0631\u0645\u0632")]
    public async Task ATokenOutsidePrintableAscii_IsRejected_WithoutACall(string token)
    {
        var verdict = await Verify(BotDefenseTestDoubles.Settings(), token: token);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        verdict.Codes.Should().Equal("token-malformed");
        _client.Calls.Should().BeEmpty("Cloudflare's tokens are printable ASCII; anything else is refused before the call");
    }

    [Fact]
    public async Task TheChallengesAge_IsMeasuredOnCloudflaresClock_NotTheServers()
    {
        var solvedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        // The server's clock runs ten minutes ahead: by it, every token is too old.
        var aheadOfCloudflare = new ManualTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10));

        _client.Answers(BotDefenseTestDoubles.Accepted(
            BotDefenseActions.Login, solvedAt: solvedAt, respondedAt: solvedAt.AddSeconds(20)));
        (await Verify(BotDefenseTestDoubles.Settings(), clock: aheadOfCloudflare)).Kind
            .Should().Be(BotDefenseVerdictKind.Passed, "20 seconds by Cloudflare's own clock");

        _client.Answers(BotDefenseTestDoubles.Accepted(
            BotDefenseActions.Login, solvedAt: solvedAt, respondedAt: solvedAt.AddSeconds(301)));
        var old = await Verify(BotDefenseTestDoubles.Settings(), clock: aheadOfCloudflare);
        old.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        old.Codes.Should().Equal("challenge-too-old");
    }

    [Fact]
    public async Task NoAnswerFromCloudflare_IsUnavailable()
    {
        _client.Answers(BotDefenseTestDoubles.NoAnswer("timeout"));

        var verdict = await Verify(BotDefenseTestDoubles.Settings());

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Unavailable);
        verdict.Codes.Should().Contain("timeout");
    }

    [Theory]
    [InlineData("invalid-input-secret", BotDefenseVerdictKind.Misconfigured)]
    [InlineData("missing-input-secret", BotDefenseVerdictKind.Misconfigured)]
    // A per-request answer is the token's (review of PR 167): only the two secret codes are the
    // server's state, so no token can steer a request into the outage allowance.
    [InlineData("bad-request", BotDefenseVerdictKind.Rejected)]
    [InlineData("internal-error", BotDefenseVerdictKind.Unavailable)]
    [InlineData("invalid-input-response", BotDefenseVerdictKind.Rejected)]
    [InlineData("timeout-or-duplicate", BotDefenseVerdictKind.Rejected)]
    [InlineData("missing-input-response", BotDefenseVerdictKind.Rejected)]
    public async Task CloudflaresRefusal_IsClassifiedByItsCode(string code, BotDefenseVerdictKind expected)
    {
        _client.Answers(BotDefenseTestDoubles.Refused(code));

        (await Verify(BotDefenseTestDoubles.Settings())).Kind.Should().Be(expected);
    }

    [Fact]
    public async Task AnAcceptedToken_ForThisSurfaceAndAFirstPartyHost_Passes()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.Login, hostname: "Accounts.Example.com"));

        var verdict = await Verify(BotDefenseTestDoubles.Settings());

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Passed);
        verdict.Hostname.Should().Be("Accounts.Example.com");
        _client.Calls.Single().Should().Be((BotDefenseTestDoubles.Secret, "a-token"));
    }

    [Fact]
    public async Task AHostOnlyInTheCorsList_IsRejected_WhileTheFirstPartyListIsSet()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.Login, hostname: "partner.example.com"));

        var verdict = await Verify(
            BotDefenseTestDoubles.Settings(),
            firstParty: BotDefenseTestDoubles.FirstParty("https://accounts.example.com"),
            cors: BotDefenseTestDoubles.Cors("https://accounts.example.com", "https://partner.example.com"));

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        verdict.Codes.Should().Contain("hostname-mismatch");
    }

    [Fact]
    public async Task WithNoFirstPartyList_TheCorsHostsAreAccepted()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.Login, hostname: "partner.example.com"));

        var verdict = await Verify(
            BotDefenseTestDoubles.Settings(),
            firstParty: BotDefenseTestDoubles.FirstParty(),
            cors: BotDefenseTestDoubles.Cors("https://partner.example.com"));

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Passed);
    }

    [Fact]
    public async Task AnotherSurfacesAction_IsRejected()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.EmailCode));

        var verdict = await Verify(BotDefenseTestDoubles.Settings(), action: BotDefenseActions.Login);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        verdict.Codes.Should().Contain("action-mismatch");
    }

    [Fact]
    public async Task AChallengeOlderThan300Seconds_IsRejected()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
        _client.Answers(
            BotDefenseTestDoubles.Accepted(BotDefenseActions.Login, solvedAt: clock.Now.AddSeconds(-301)),
            BotDefenseTestDoubles.Accepted(BotDefenseActions.Login, solvedAt: clock.Now.AddSeconds(-299)));

        (await Verify(BotDefenseTestDoubles.Settings(), clock: clock)).Kind.Should().Be(BotDefenseVerdictKind.Rejected);
        (await Verify(BotDefenseTestDoubles.Settings(), clock: clock)).Kind.Should().Be(BotDefenseVerdictKind.Passed);
    }

    [Fact]
    public async Task ATestingSecretInProduction_NeverReachesCloudflare_EvenWithARealToken()
    {
        var verdict = await Verify(
            BotDefenseTestDoubles.Settings(secret: "1x0000000000000000000000000000000AA"), token: "a-real-looking-token", production: true);

        verdict.Kind.Should().Be(BotDefenseVerdictKind.Misconfigured);
        _client.Calls.Should().BeEmpty();
    }
    // ---- OD2 (REVIEW 167): no token while Cloudflare looks down is the outage, not the visitor ----

    private Task<BotDefenseVerdict> VerifyNoToken(BotDefenseOutageSignal outage, TimeProvider clock) =>
        BotDefenseTestDoubles.Verifier(BotDefenseTestDoubles.Settings(), _client, timeProvider: clock, outage: outage)
            .VerifyAsync(null, BotDefenseActions.Login, CancellationToken.None);

    [Fact]
    public async Task NoToken_WhileTheBreakerIsTripped_IsAnOutage_UntilAnAnswerClosesIt()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-13T10:00:00Z"));
        var breaker = new BotDefenseCircuitBreaker(clock, NullLogger<BotDefenseCircuitBreaker>.Instance);
        var outage = BotDefenseTestDoubles.Outage(clock, breaker);
        for (var i = 0; i < BotDefenseCircuitBreaker.FailureThreshold; i++)
        {
            breaker.RecordFailure("timeout");
        }

        var verdict = await VerifyNoToken(outage, clock);
        verdict.Kind.Should().Be(BotDefenseVerdictKind.Unavailable, "the fail mode decides while Cloudflare does not answer");
        verdict.Codes.Should().Equal("token-missing-cloudflare-unreachable");
        _client.Calls.Should().BeEmpty();

        clock.Advance(BotDefenseCircuitBreaker.OpenDuration + TimeSpan.FromSeconds(1));
        (await VerifyNoToken(outage, clock)).Kind.Should().Be(BotDefenseVerdictKind.Unavailable,
            "past the open period the breaker waits for the call that would close it");

        breaker.RecordAnswer();
        (await VerifyNoToken(outage, clock)).Kind.Should().Be(BotDefenseVerdictKind.Missing);
    }

    [Fact]
    public void TheFailedProbeWindow_OutlastsTheNextProbe()
    {
        // REVIEW-FIXES 167 F13: the next probe starts at the first tick after the cache lapses and
        // records its answer only when it ends; a window equal to the cache left those seconds bare.
        var cadence = new Auth.Infrastructure.Security.BotDefensePostureContributor(
            Auth_API.Tests.Helpers.TestHelpers.CreateOptions(BotDefenseTestDoubles.Settings()),
            null!, new FakeEnvironment(false), null!, null!, null!, TimeProvider.System).ReevaluateEvery!.Value;

        BotDefenseOutageSignal.ProbeWindow.Should().BeGreaterThan(
            Auth.Infrastructure.Security.BotDefensePostureContributor.ProbeCacheDuration + cadence);
        BotDefenseOutageSignal.ProbeWindow.Should().Be(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task NoToken_AfterTheReachabilityProbeGotNoAnswer_IsAnOutage_ForSevenMinutes()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-13T10:00:00Z"));
        var outage = BotDefenseTestDoubles.Outage(clock);
        outage.RecordProbe(answered: false);

        (await VerifyNoToken(outage, clock)).Kind.Should().Be(BotDefenseVerdictKind.Unavailable,
            "in a full outage no token reaches the server, so only the probe sees it");
        clock.Advance(BotDefenseOutageSignal.ProbeWindow - TimeSpan.FromSeconds(1));
        (await VerifyNoToken(outage, clock)).Kind.Should().Be(BotDefenseVerdictKind.Unavailable);
        clock.Advance(TimeSpan.FromSeconds(2));
        (await VerifyNoToken(outage, clock)).Kind.Should().Be(BotDefenseVerdictKind.Missing);
    }

    [Fact]
    public async Task NoToken_WhileCloudflareAnswers_IsAMissingToken()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-13T10:00:00Z"));
        var neverProbed = BotDefenseTestDoubles.Outage(clock);
        var recovered = BotDefenseTestDoubles.Outage(clock);
        recovered.RecordProbe(answered: false);
        recovered.RecordProbe(answered: true);

        (await VerifyNoToken(neverProbed, clock)).Kind.Should().Be(BotDefenseVerdictKind.Missing);
        (await VerifyNoToken(recovered, clock)).Kind.Should().Be(BotDefenseVerdictKind.Missing,
            "an answered probe clears the outage at once");
    }

}
