using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Infrastructure.Configuration;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security;

/// <summary>
/// S09 (T10): the breached-password posture. A weakened setting or an HIBP that does not answer
/// shows to no user, so the log states it: Warning in Production, information elsewhere, and
/// only when it changed.
/// </summary>
public class BreachedPasswordPostureContributorTests
{
    private const string WeakenedPrefix = "Breached-password screening weakened in Production: ";
    private const string ProbeFailedPrefix = "HIBP probe call failed (";

    private static Mock<IHostEnvironment> Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment;
    }

    private static (BreachedPasswordPostureContributor Contributor, Mock<IBreachedPasswordChecker> Checker) Create(
        BreachedPasswordCheckSettings settings, string environment = "Production")
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(3614);

        var services = new ServiceCollection();
        services.AddScoped(_ => checker.Object);
        var root = services.BuildServiceProvider();

        var contributor = new BreachedPasswordPostureContributor(
            TestHelpers.CreateOptions(new PasswordSettings { BreachedPasswordCheck = settings }),
            root.GetRequiredService<IServiceScopeFactory>(),
            Environment(environment).Object,
            TimeProvider.System);
        return (contributor, checker);
    }

    /// <summary>The lines as the posture check writes them.</summary>
    private static IReadOnlyList<CapturedLogEntry> Render(SecurityPosture posture)
    {
        var logger = new CapturingLogger<BreachedPasswordPostureContributorTests>();
        foreach (var line in posture.Lines)
        {
#pragma warning disable CA2254
            logger.Log(line.Level, line.Exception, line.MessageTemplate, line.Arguments.ToArray());
#pragma warning restore CA2254
        }

        return logger.Entries;
    }

    public static TheoryData<string, BreachedPasswordCheckSettings, string> Weakenings => new()
    {
        { "Enabled", new BreachedPasswordCheckSettings { Enabled = false }, "Enabled=False" },
        { "Mode", new BreachedPasswordCheckSettings { Mode = BreachAction.Warn }, "Mode=Warn" },
        { "TimeoutMs", new BreachedPasswordCheckSettings { TimeoutMs = 499 }, "TimeoutMs=499" },
        { "RejectThreshold", new BreachedPasswordCheckSettings { RejectThreshold = 2 }, "RejectThreshold=2" },
    };

    [Theory]
    [MemberData(nameof(Weakenings))]
    public async Task Production_EachWeakening_IsOneWarning(
        string field, BreachedPasswordCheckSettings settings, string reason)
    {
        var (contributor, _) = Create(settings);

        var posture = await contributor.EvaluateAsync(CancellationToken.None);
        var lines = Render(posture);

        posture.Level.Should().Be(SecurityPostureLevel.Weak, field);
        lines.Where(l => l.Level >= LogLevel.Warning).Should().ContainSingle()
            .Which.Message.Should().StartWith(WeakenedPrefix).And.Contain(reason);
        posture.Reasons.Should().ContainSingle().Which.Should().StartWith(reason);
    }

    [Fact]
    public async Task Production_Defaults_AreOk_AndStateThePostureAndTheProbe()
    {
        var (contributor, checker) = Create(new BreachedPasswordCheckSettings());

        var posture = await contributor.EvaluateAsync(CancellationToken.None);
        var lines = Render(posture);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Reasons.Should().BeEmpty();
        lines.Should().OnlyContain(l => l.Level == LogLevel.Information);
        lines.Select(l => l.Message).Should().SatisfyRespectively(
            first => first.Should().Be(
                "Breached-password posture: Enabled=True, Mode=Enforce, FailOpen=True, RejectThreshold=1, TimeoutMs=2000"),
            second => second.Should().MatchRegex(@"^HIBP probe call: reachable in \d+ ms$"));
        checker.Verify(c => c.GetBreachCountAsync("Summer2024!", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Development_Weakened_WritesInformationOnly()
    {
        var (contributor, checker) = Create(
            new BreachedPasswordCheckSettings { Enabled = false, Mode = BreachAction.Warn }, "Development");

        var posture = await contributor.EvaluateAsync(CancellationToken.None);
        var lines = Render(posture);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        lines.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
        checker.Verify(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a disabled check makes no external call, the probe included");
    }

    public static TheoryData<string, Func<Mock<IBreachedPasswordChecker>, Task>, string> ProbeFailures => new()
    {
        {
            "throws",
            checker =>
            {
                checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("no route to host"));
                return Task.CompletedTask;
            },
            "Error"
        },
        {
            "answers 0",
            checker =>
            {
                checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(0);
                return Task.CompletedTask;
            },
            "NotListed"
        },
        {
            "answers with no listing (a block page)",
            checker =>
            {
                checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidDataException("HIBP answer carried no range listing"));
                return Task.CompletedTask;
            },
            "NotListed"
        },
        {
            "runs out of time",
            checker =>
            {
                checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new OperationCanceledException());
                return Task.CompletedTask;
            },
            "Timeout"
        },
    };

    [Theory]
    [MemberData(nameof(ProbeFailures))]
    public async Task Production_ProbeFailure_IsAWarning(
        string _, Func<Mock<IBreachedPasswordChecker>, Task> arrange, string failureKind)
    {
        var (contributor, checker) = Create(new BreachedPasswordCheckSettings());
        await arrange(checker);

        var posture = await contributor.EvaluateAsync(CancellationToken.None);
        var lines = Render(posture);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Probe!.Outcome.Should().Be(failureKind);
        lines.Where(l => l.Level >= LogLevel.Warning).Should().ContainSingle()
            .Which.Message.Should().MatchRegex($@"^HIBP probe call failed \({failureKind}\) after \d+ ms$");
    }

    /// <summary>
    /// The owner's dev run (s09-egress-block) reads this line in a Development log, so the failure
    /// is written there too, at information.
    /// </summary>
    [Fact]
    public async Task Development_ProbeFailure_IsWrittenAtInformation()
    {
        var (contributor, checker) = Create(new BreachedPasswordCheckSettings(), "Development");
        checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var posture = await contributor.EvaluateAsync(CancellationToken.None);
        var lines = Render(posture);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        lines.Should().OnlyContain(l => l.Level == LogLevel.Information);
        lines.Should().Contain(l => l.Message.StartsWith(ProbeFailedPrefix + "Timeout)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The host stopping is not a probe failure: it propagates, and the posture check ends quietly.
    /// </summary>
    [Fact]
    public async Task HostStopping_DuringProbe_Propagates()
    {
        var (contributor, checker) = Create(new BreachedPasswordCheckSettings());
        using var stopping = new CancellationTokenSource();
        checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await stopping.CancelAsync();
                throw new OperationCanceledException(stopping.Token);
            });

        var act = () => contributor.EvaluateAsync(stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Each evaluation probes through a scope of its own, as a request would, so a scoped or
    /// transient checker (the typed HttpClient) is never held across evaluations.
    /// </summary>
    [Fact]
    public async Task Probe_UsesANewScopePerEvaluation()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddScoped<IBreachedPasswordChecker>(_ =>
        {
            Interlocked.Increment(ref created);
            var checker = new Mock<IBreachedPasswordChecker>();
            checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
            return checker.Object;
        });
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var contributor = new BreachedPasswordPostureContributor(
            TestHelpers.CreateOptions(new PasswordSettings()),
            root.GetRequiredService<IServiceScopeFactory>(),
            Environment("Production").Object,
            TimeProvider.System);

        await contributor.EvaluateAsync(CancellationToken.None);
        await contributor.EvaluateAsync(CancellationToken.None);

        created.Should().Be(2);
    }

    /// <summary>
    /// A reload is global: any section, the periodic refresh, an edited file. The breach lines are
    /// written again only when the breach posture changed; a reload that changes nothing in it,
    /// or a probe that only took a different time, writes nothing. Runs the real chain: the
    /// database overrides provider, the options it rebinds, this contributor and the posture check.
    /// </summary>
    [Fact]
    public async Task ReloadWithoutChange_WritesNoLine()
    {
        var overrides = new List<(string SectionKey, string OverridesJson)>
        {
            ("Password", """{"BreachedPasswordCheck":{"TimeoutMs":2000}}"""),
        };
        var provider = new DbSettingsConfigurationProvider(() => [.. overrides], new Dictionary<string, int>());
        var configuration = new ConfigurationBuilder().Add(new FixedProviderSource(provider)).Build();

        var probes = 0;
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(c => c.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref probes);
                return Task.FromResult(3614);
            });

        var services = new ServiceCollection();
        services.Configure<PasswordSettings>(configuration.GetSection(PasswordSettings.SectionName));
        services.AddScoped(_ => checker.Object);
        services.AddSingleton(Environment("Production").Object);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISecurityPostureContributor, BreachedPasswordPostureContributor>();
        await using var root = services.BuildServiceProvider();

        var logger = new CapturingLogger<SecurityPostureCheck>();
        using var check = new SecurityPostureCheck(
            root.GetServices<ISecurityPostureContributor>(), configuration, TimeProvider.System, logger,
            TimeSpan.FromMilliseconds(20));

        await check.StartAsync(CancellationToken.None);
        await WaitUntil(() => Volatile.Read(ref probes) == 1 && logger.Entries.Count == 2);

        // Same breach settings, another section's reload: evaluated, nothing written.
        configuration.Reload();
        await WaitUntil(() => Volatile.Read(ref probes) == 2);
        await Task.Delay(100);
        logger.Entries.Should().HaveCount(2);

        // A weakening saved in the console: written, with its Warning.
        overrides[0] = ("Password", """{"BreachedPasswordCheck":{"TimeoutMs":2000,"Mode":"Warn"}}""");
        provider.Reload();
        await WaitUntil(() => logger.Entries.Count == 5);
        logger.Entries.Skip(2).Select(e => e.Message).Should().SatisfyRespectively(
            posture => posture.Should().Contain("Mode=Warn"),
            weakened => weakened.Should().StartWith(WeakenedPrefix + "Mode=Warn"),
            probe => probe.Should().StartWith("HIBP probe call: reachable"));
        logger.Entries.Where(e => e.Level == LogLevel.Warning).Should().ContainSingle();

        await check.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the posture check should have evaluated by now");
            await Task.Delay(10);
        }
    }

    /// <summary>A configuration source that hands the builder an existing provider.</summary>
    private sealed class FixedProviderSource(IConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }
}
