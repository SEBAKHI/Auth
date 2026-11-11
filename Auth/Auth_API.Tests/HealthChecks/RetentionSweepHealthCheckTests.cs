using Auth.Application.Configuration;
using Auth.Infrastructure.AccountDeletion;
using Auth.Shared.Diagnostics;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// <c>/ready</c> reports the retention sweep of this process. The longest healthy
/// gap is one UTC day, plus one poll, plus an hour; past it the check reports the
/// registration's failure status (Degraded, which still answers 200). Before the
/// first success it says the sweep has not completed, never that it ran.
/// </summary>
/// <remarks>Deliberate break: answer Healthy whatever the status; the overdue tests turn red.</remarks>
public class RetentionSweepHealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    // Default poll: 15 minutes, so the window is 24 h + 15 min + 1 h.
    private static readonly TimeSpan Window = TimeSpan.FromHours(25) + TimeSpan.FromMinutes(15);

    private readonly AdjustableTimeProvider _time = new();
    private readonly RetentionSweepStatus _status;

    public RetentionSweepHealthCheckTests()
    {
        // Freeze first, then move: one read of the real clock, so the start is exact.
        _time.Advance(TimeSpan.Zero);
        _time.Advance(Start - _time.GetUtcNow());
        _status = new RetentionSweepStatus(_time);
    }

    private async Task<HealthCheckResult> Check()
    {
        var check = new RetentionSweepHealthCheck(
            _status, TestHelpers.CreateOptions(new AccountDeletionSettings()), _time);
        return await check.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("retention-sweep", check, HealthStatus.Degraded, ["ready"]),
        });
    }

    [Fact]
    public async Task NoSuccessYet_IsHealthy_AndSaysTheSweepHasNotCompleted()
    {
        // A young process is Healthy whatever its sweep did, so the description
        // must not claim a run that has not happened.
        _time.Advance(Window);

        var result = await Check();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Retention sweep has not completed since this process started.");
    }

    [Fact]
    public async Task ASuccessWithinTheWindow_SaysItRan()
    {
        _status.RecordSuccess(_time.UtcNow);
        _time.Advance(TimeSpan.FromHours(3));

        var result = await Check();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Retention sweep ran within its window.");
    }

    [Fact]
    public async Task NoSuccessSinceStart_IsDegraded_PastTheWindow()
    {
        // A sweep that fails from its very first run never records a success:
        // the gap is measured from the start of the process.
        _time.Advance(Window + TimeSpan.FromMinutes(1));

        var result = await Check();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Retention sweep overdue.");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task LastSuccessOlderThanTheWindow_IsDegraded()
    {
        _time.Advance(TimeSpan.FromHours(2));
        _status.RecordSuccess(_time.UtcNow);
        _time.Advance(Window + TimeSpan.FromMinutes(1));

        (await Check()).Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task ARecentSuccess_IsHealthy_EvenLongAfterTheStart()
    {
        _time.Advance(TimeSpan.FromDays(10));
        _status.RecordSuccess(_time.UtcNow);
        _time.Advance(TimeSpan.FromHours(20));

        (await Check()).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task ASettingsFailure_NeverReachesTheAnonymousResponse()
    {
        // Left to the health-check service, a thrown exception becomes the
        // entry's description, and the description is written to anonymous
        // callers even with error details switched off.
        var settings = new Mock<IOptionsMonitor<AccountDeletionSettings>>();
        settings.SetupGet(s => s.CurrentValue).Throws(new InvalidOperationException("marker-7f3"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_status);
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(settings.Object);
        services.AddHealthChecks()
            .AddCheck<RetentionSweepHealthCheck>("retention-sweep", failureStatus: HealthStatus.Degraded, tags: ["ready"]);
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["retention-sweep"].Status.Should().Be(HealthStatus.Degraded);
        HealthCheckJsonFormatter.Serialize(report, includeErrorDetails: false).Should().NotContain("marker-7f3");
    }

    [Fact]
    public void TheRegistrationInProgram_IsDegradedNotUnhealthy()
    {
        // Unhealthy answers 503, which would take the API out of the gateway's
        // rotation over a sweep that has no bearing on sign-in.
        var program = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Program.cs"));
        var start = program.IndexOf(".AddCheck<RetentionSweepHealthCheck>(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the retention-sweep check must be registered");
        var end = program.IndexOf(';', start);

        program[start..end].Should().Contain("failureStatus: HealthStatus.Degraded");
    }

    [Theory]
    [InlineData(15, 25 * 60 + 15)]
    [InlineData(1440, 49 * 60)]
    public void TheWindow_IsADayPlusOnePollPlusAnHour(int pollMinutes, int expectedMinutes)
    {
        // The daily sweep runs on the first poll after midnight, so a longer
        // poll can only delay it; the window allows for that and for the run.
        RetentionSweepStatus.MaxHealthyGap(TimeSpan.FromMinutes(pollMinutes))
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
