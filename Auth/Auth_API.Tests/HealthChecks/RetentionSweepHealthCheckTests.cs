using Auth.Application.Configuration;
using Auth.Infrastructure.AccountDeletion;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// <c>/ready</c> reports whether the retention sweep still runs. The sweep writes no
/// audit row when it deletes nothing, so this check is the only thing that tells
/// "nothing to delete" apart from "not running". The longest healthy gap is one
/// UTC day, plus one poll, plus an hour; past it the check reports the
/// registration's failure status (Degraded, which still answers 200).
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
    public async Task NoSuccessYet_IsHealthy_WithinTheWindowAfterStart()
    {
        _time.Advance(Window);

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

    [Theory]
    [InlineData(15, 25 * 60 + 15)]
    [InlineData(1440, 49 * 60)]
    public void TheWindow_IsADayPlusOnePollPlusAnHour(int pollMinutes, int expectedMinutes)
    {
        // The daily sweep runs on the first poll after midnight, so with a
        // one-day poll two successes can lie up to 48 hours apart.
        RetentionSweepStatus.MaxHealthyGap(TimeSpan.FromMinutes(pollMinutes))
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }
}
