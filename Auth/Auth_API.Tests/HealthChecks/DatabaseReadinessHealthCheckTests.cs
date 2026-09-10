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

    /// <summary>
    /// The database is published from Visual Studio, the API is uploaded
    /// separately, and the two have gone out in the wrong order before. Every
    /// schema change the code cannot run without is listed on the check, so an
    /// API uploaded ahead of its database says so on /ready instead of failing
    /// inside the first request that reaches the missing column.
    /// </summary>
    [Fact]
    public void TheSchemaExpectations_CoverTheUsernameWidth()
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().Contain(
            expectation => expectation.Sql.Contains("COL_LENGTH('dbo.Users', 'Username')")
                           && expectation.Sql.Contains("510"),
            "Username became the full address and NVARCHAR(255) is 510 bytes; " +
            "an API that writes it into a 50-character column fails with a truncation error the middleware does not map");
    }

    [Fact]
    public void TheSchemaExpectations_CoverThePendingRegistrationsTable()
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().Contain(
            expectation => expectation.Sql.Contains("OBJECT_ID('dbo.PendingRegistrations', 'U')"),
            "the daily sweep and the registration flow both fail with 'Invalid object name' when the table was not published");
    }

    [Fact]
    public void ReadinessReportsAMissingSchemaHalf_ByName()
    {
        var expectation = DatabaseReadinessHealthCheck.SchemaExpectations[0];

        var missing = DatabaseReadinessHealthCheck.SchemaShortfall(expectation, 0);
        var present = DatabaseReadinessHealthCheck.SchemaShortfall(expectation, 1);
        var presentAsBigint = DatabaseReadinessHealthCheck.SchemaShortfall(expectation, 1L);

        missing.Should().NotBeNull();
        missing!.Value.Status.Should().Be(HealthStatus.Degraded);
        missing.Value.Exception.Should().NotBeNull();
        missing.Value.Exception!.Message.Should().Contain(expectation.Name,
            "the operator must be told which half is behind, not just that something is");
        missing.Value.Description.Should().NotContain("Users.Username",
            "/ready answers anonymous callers; the object name belongs on the gated error channel, not the public description");
        missing.Value.Description.Should().Contain("Publish Auth_DB before this API");
        present.Should().BeNull();
        presentAsBigint.Should().BeNull("SQL Server may hand the CASE result back as either int or bigint");
    }
}
