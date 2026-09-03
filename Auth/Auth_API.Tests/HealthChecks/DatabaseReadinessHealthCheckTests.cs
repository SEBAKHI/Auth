using Auth_API.Common.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// The readiness probe must be the one thing an anonymous burst cannot
/// multiply: one database round trip per window, shared by every caller.
/// </summary>
public class DatabaseReadinessHealthCheckTests
{
    private static readonly HealthCheckContext Context = new();

    [Fact]
    public async Task ConcurrentCallers_ShareOneProbe()
    {
        // Arrange — a probe that blocks until released, so every caller piles up on it
        var probeStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var probes = 0;
        var check = new DatabaseReadinessHealthCheck(
            async _ =>
            {
                Interlocked.Increment(ref probes);
                probeStarted.TrySetResult();
                await release.Task;
                return HealthCheckResult.Healthy("ok");
            },
            TimeSpan.FromSeconds(5),
            () => new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        // Act — fifty callers at once, released together
        var callers = Enumerable.Range(0, 50)
            .Select(_ => check.CheckHealthAsync(Context))
            .ToArray();
        await probeStarted.Task;
        release.SetResult();
        var results = await Task.WhenAll(callers);

        // Assert
        probes.Should().Be(1, "every concurrent caller must wait for the single in-flight probe");
        results.Should().OnlyContain(r => r.Status == HealthStatus.Healthy);
    }

    [Fact]
    public async Task WithinTheWindow_TheDatabaseIsNotAskedAgain()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var probes = 0;
        var check = new DatabaseReadinessHealthCheck(
            _ => { probes++; return Task.FromResult(HealthCheckResult.Healthy("ok")); },
            TimeSpan.FromSeconds(5),
            () => now);

        // Act
        await check.CheckHealthAsync(Context);
        now = now.AddSeconds(4);
        await check.CheckHealthAsync(Context);
        await check.CheckHealthAsync(Context);

        // Assert
        probes.Should().Be(1);
    }

    [Fact]
    public async Task AfterTheWindow_TheDatabaseIsAskedOnceMore()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var probes = 0;
        var check = new DatabaseReadinessHealthCheck(
            _ => { probes++; return Task.FromResult(HealthCheckResult.Healthy("ok")); },
            TimeSpan.FromSeconds(5),
            () => now);

        // Act
        await check.CheckHealthAsync(Context);
        now = now.AddSeconds(5);
        await check.CheckHealthAsync(Context);

        // Assert
        probes.Should().Be(2);
    }

    [Fact]
    public async Task AFailure_IsCachedLikeASuccess_SoAnOutageIsNotHammered()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var probes = 0;
        var check = new DatabaseReadinessHealthCheck(
            _ => { probes++; return Task.FromResult(HealthCheckResult.Degraded("down")); },
            TimeSpan.FromSeconds(5),
            () => now);

        // Act
        var first = await check.CheckHealthAsync(Context);
        now = now.AddSeconds(1);
        var second = await check.CheckHealthAsync(Context);

        // Assert
        probes.Should().Be(1);
        first.Status.Should().Be(HealthStatus.Degraded);
        second.Status.Should().Be(HealthStatus.Degraded);
    }
}
