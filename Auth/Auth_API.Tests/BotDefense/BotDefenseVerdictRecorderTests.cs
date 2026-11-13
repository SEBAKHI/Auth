using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.BotDefense;
using Auth_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 (BD12, review of PR 167): one line per verdict, at Warning when the check could not run or
/// when the deployment's own setup refused the token (a host, an action, a clock), and counted as
/// such for the posture's <c>bot.local-rejection-spike</c>. A robot's junk token stays at
/// Information and is counted apart, never as a submission. Deliberate breaks, verified red: the
/// local codes left at Information; a junk token counted as a submission.
/// </summary>
public class BotDefenseVerdictRecorderTests
{
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger _logger = new();
    private readonly BotDefenseVerdictCounts _counts;

    public BotDefenseVerdictRecorderTests()
    {
        _counts = new BotDefenseVerdictCounts(_clock);
    }

    private BotDefenseVerdictRecorder Recorder(string? origin = "https://console.example.com") =>
        new(
            new FixedOrigin(origin),
            TestHelpers.CreateOptions(new IdentityProviderSettings
            {
                FirstPartySpaOrigins = ["https://accounts.example.com", "https://console.example.com"],
            }),
            new ConfigurationBuilder().Build(),
            _counts,
            _logger);

    private static BotDefenseVerdict Rejected(params string[] codes) =>
        new(BotDefenseVerdictKind.Rejected, codes, "console.example.com", 1, 40);

    [Theory]
    [InlineData("hostname-mismatch")]
    [InlineData("action-mismatch")]
    [InlineData("challenge-too-old")]
    public void ARefusalTheDeploymentsSetupCaused_IsAWarning_AndCountedAsSuch(string code)
    {
        Recorder().Record(BotDefenseActions.Login, BotDefenseMode.Enforce, Rejected(code));

        _logger.Levels.Should().Equal(LogLevel.Warning);
        _counts.EnforcedWindow().LocalRejected.Should().Be(1);
    }

    [Theory]
    [InlineData("invalid-input-response")]
    [InlineData("token-too-long")]
    [InlineData("token-malformed")]
    [InlineData("dummy-token")]
    public void AJunkToken_StaysAtInformation_AndIsCountedApart(string code)
    {
        Recorder().Record(BotDefenseActions.Login, BotDefenseMode.Enforce, Rejected(code));
        Recorder().Record(BotDefenseActions.Login, BotDefenseMode.Monitor, Rejected(code));

        _logger.Levels.Should().Equal(LogLevel.Information, LogLevel.Information);
        _counts.EnforcedWindow().Should().Be((0, 0, 0), "junk neither dilutes nor makes a window count");
        _counts.PassRates()["https://console.example.com"].Should().Be((0L, 0L, 2L), "junk does not lower the app's pass rate");
    }

    [Fact]
    public void ARefusalOfARealToken_IsASubmission()
    {
        Recorder().Record(BotDefenseActions.Login, BotDefenseMode.Enforce, Rejected("timeout-or-duplicate"));

        _counts.EnforcedWindow().Should().Be((1, 0, 0));
        _counts.PassRates()["https://console.example.com"].Should().Be((1L, 0L, 0L));
    }

    [Fact]
    public void TheLine_NamesTheSurfaceTheVerdictAndTheApp_NeverTheToken()
    {
        Recorder().Record(BotDefenseActions.EmailCode, BotDefenseMode.Monitor, BotDefenseVerdict.Local(BotDefenseVerdictKind.Missing));

        _logger.Messages.Single().Should().Be(
            "Bot defense verdict: action email-code verdict Missing mode Monitor origin https://console.example.com hostname - codes - attempts 0 elapsedMs 0");
    }

    private sealed class FixedOrigin(string? origin) : IRequestOriginContext
    {
        public string? Origin => origin;
    }

    private sealed class CapturingLogger : ILogger<BotDefenseVerdictRecorder>
    {
        public List<LogLevel> Levels { get; } = [];

        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
