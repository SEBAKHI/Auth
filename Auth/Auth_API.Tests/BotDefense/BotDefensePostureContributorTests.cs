using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Infrastructure.BotDefense;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS11 (BD12): the posture's seven identifiers with their severities. In Production, Off is
/// Critical, Monitor a Warning; a key missing while the check is on is Critical; a testing key is
/// Critical; a secret Cloudflare refuses is Critical and no answer a Warning, with the round trip
/// written; the pass rate per app is reported; more than half of at least 20 enforced
/// submissions in ten minutes without a token is Critical.
/// </summary>
/// <remarks>Deliberate break, verified red: a missing key under Monitor in Production at Warning instead of Critical.</remarks>
public class BotDefensePostureContributorTests
{
    private readonly FakeSiteverifyClient _client = new();
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
    private readonly BotDefenseVerdictCounts _counts;

    public BotDefensePostureContributorTests()
    {
        _counts = new BotDefenseVerdictCounts(_clock);
    }

    private BotDefensePostureContributor Contributor(BotDefenseSettings settings, bool production)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITurnstileSiteverifyClient>(_client);
        return new BotDefensePostureContributor(
            TestHelpers.CreateOptions(settings),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FakeEnvironment(production),
            _counts,
            _clock);
    }

    private static SecurityPostureLine Line(SecurityPosture posture, string identifier) =>
        posture.Lines.Single(line => line.MessageTemplate.Contains(identifier + ":", StringComparison.Ordinal));

    [Theory]
    [InlineData(BotDefenseMode.Off, LogLevel.Critical)]
    [InlineData(BotDefenseMode.Monitor, LogLevel.Warning)]
    [InlineData(BotDefenseMode.Enforce, LogLevel.Information)]
    public async Task BotMode_InProduction(BotDefenseMode mode, LogLevel expected)
    {
        _client.Answers(BotDefenseTestDoubles.Refused("invalid-input-response"));

        var posture = await Contributor(BotDefenseTestDoubles.Settings(mode), production: true).EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.mode").Level.Should().Be(expected);
        Line(posture, "bot.fail-mode").Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task BotMode_OutsideProduction_IsInformation()
    {
        var posture = await Contributor(BotDefenseTestDoubles.Settings(BotDefenseMode.Off, secret: ""), production: false)
            .EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.mode").Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task AMissingKey_WhileTheCheckIsOn_IsCriticalInProduction()
    {
        var posture = await Contributor(BotDefenseTestDoubles.Settings(BotDefenseMode.Monitor, secret: ""), production: true)
            .EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.keys-missing").Level.Should().Be(LogLevel.Critical);
        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Reasons.Should().Contain("bot.keys-missing");
    }

    [Fact]
    public async Task AMissingKey_WhileOff_IsInformation()
    {
        var posture = await Contributor(BotDefenseTestDoubles.Settings(BotDefenseMode.Off, siteKey: "", secret: ""), production: false)
            .EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.keys-missing").Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task ATestingKeyInProduction_IsCritical()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted("test"));

        var posture = await Contributor(BotDefenseTestDoubles.Settings(siteKey: TurnstileTestingKeys.Sitekeys[0]), production: true)
            .EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.testing-key-in-production").Level.Should().Be(LogLevel.Critical);
    }

    [Fact]
    public async Task Reachability_SendsTheDummyToken_AndWritesTheRoundTrip()
    {
        _client.Answers(BotDefenseTestDoubles.Refused("invalid-input-response"));

        var posture = await Contributor(BotDefenseTestDoubles.Settings(BotDefenseMode.Off), production: true)
            .EvaluateAsync(CancellationToken.None);

        _client.Calls.Single().Should().Be((BotDefenseTestDoubles.Secret, TurnstileTestingKeys.DummyToken),
            "probed while Off too: the owner reads it before switching Monitor on");
        var line = Line(posture, "bot.siteverify-reachability");
        line.Level.Should().Be(LogLevel.Information);
        line.Arguments.Should().Contain(40L);
        posture.Probe!.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Reachability_ASecretCloudflareRefuses_IsCriticalInProduction()
    {
        _client.Answers(BotDefenseTestDoubles.Refused("invalid-input-secret"));

        var posture = await Contributor(BotDefenseTestDoubles.Settings(), production: true).EvaluateAsync(CancellationToken.None);

        Line(posture, "bot.siteverify-reachability").Level.Should().Be(LogLevel.Critical);
    }

    [Fact]
    public async Task Reachability_NoAnswer_IsAWarningInProduction_AndIsCachedFiveMinutes()
    {
        _client.Answers(BotDefenseTestDoubles.NoAnswer("timeout"), BotDefenseTestDoubles.Refused("invalid-input-response"));
        var contributor = Contributor(BotDefenseTestDoubles.Settings(), production: true);

        Line(await contributor.EvaluateAsync(CancellationToken.None), "bot.siteverify-reachability").Level.Should().Be(LogLevel.Warning);
        await contributor.EvaluateAsync(CancellationToken.None);
        _client.Calls.Should().ContainSingle("one probe answers for five minutes");

        _clock.Advance(BotDefensePostureContributor.ProbeCacheDuration + TimeSpan.FromSeconds(1));
        Line(await contributor.EvaluateAsync(CancellationToken.None), "bot.siteverify-reachability").Level.Should().Be(LogLevel.Information);
        _client.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ThePassRate_IsReportedPerApp()
    {
        _counts.Add("https://accounts.example.com", BotDefenseMode.Monitor, BotDefenseVerdictKind.Passed);
        _counts.Add("https://accounts.example.com", BotDefenseMode.Monitor, BotDefenseVerdictKind.Missing);
        _counts.Add("https://accounts.example.com", BotDefenseMode.Monitor, BotDefenseVerdictKind.Unconfigured);

        var posture = await Contributor(BotDefenseTestDoubles.Settings(BotDefenseMode.Monitor, secret: ""), production: false)
            .EvaluateAsync(CancellationToken.None);

        var line = Line(posture, "bot.monitor-pass-rate");
        line.Level.Should().Be(LogLevel.Information);
        line.Arguments.Single().Should().Be("https://accounts.example.com 1/2 (50.0 %)", "an Unconfigured verdict is no check and is not counted");
    }

    [Fact]
    public async Task MoreThanHalfOfTwentyEnforcedSubmissionsWithoutAToken_IsCritical()
    {
        for (var i = 0; i < 9; i++)
        {
            _counts.Add("other", BotDefenseMode.Enforce, BotDefenseVerdictKind.Passed);
        }

        for (var i = 0; i < 10; i++)
        {
            _counts.Add("other", BotDefenseMode.Enforce, BotDefenseVerdictKind.Missing);
        }

        var contributor = Contributor(BotDefenseTestDoubles.Settings(secret: ""), production: true);
        Line(await contributor.EvaluateAsync(CancellationToken.None), "bot.token-missing-spike").Level
            .Should().Be(LogLevel.Information, "19 submissions say nothing (the floor is 20)");

        _counts.Add("other", BotDefenseMode.Enforce, BotDefenseVerdictKind.Missing);
        Line(await contributor.EvaluateAsync(CancellationToken.None), "bot.token-missing-spike").Level
            .Should().Be(LogLevel.Critical, "11 of 20 carried no token");

        _clock.Advance(BotDefenseVerdictCounts.SpikeWindow + TimeSpan.FromSeconds(1));
        Line(await contributor.EvaluateAsync(CancellationToken.None), "bot.token-missing-spike").Level
            .Should().Be(LogLevel.Information, "the window is ten minutes");
    }
}
