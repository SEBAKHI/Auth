using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Infrastructure.BotDefense;

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
    [InlineData("bad-request", BotDefenseVerdictKind.Misconfigured)]
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
}
