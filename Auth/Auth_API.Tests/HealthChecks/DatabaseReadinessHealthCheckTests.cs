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
    public void TheSchemaExpectations_CoverTheAuthorizationRequestsTable()
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains("OBJECT_ID('dbo.AuthorizationRequests', 'U')")
                           && expectation.Name.Contains("AuthorizationRequests"),
            "with handles on by default (OI-145), every sign-in that passes through an accounts page writes " +
            "and resumes a row, and fails with 'Invalid object name' when the table was not published");
    }

    [Theory]
    [InlineData("UiLocale")]
    [InlineData("UiTheme")]
    public void TheSchemaExpectations_CoverThePresentationHintColumns(string column)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"COL_LENGTH('dbo.AuthorizationRequests', '{column}')")
                           && expectation.Name == $"AuthorizationRequests.{column} column",
            "the pending request's insert and resume read name both columns, so an API ahead of its DACPAC " +
            "fails every sign-in through an accounts page with 'Invalid column name'");
    }

    [Theory]
    [InlineData("TwoFactorAuth", "EmailCodeEnabledAt")]
    [InlineData("TwoFactorAuth", "EmailFailedAttempts")]
    [InlineData("TwoFactorAuth", "EmailLockedUntil")]
    [InlineData("TwoFactorAuth", "RecoveryCodesQualified")]
    [InlineData("TwoFactorChallenges", "EmailCodeHash")]
    [InlineData("TwoFactorChallenges", "EmailCodeSentAt")]
    [InlineData("TwoFactorChallenges", "EmailCodeSendCount")]
    [InlineData("TwoFactorChallenges", "Purpose")]
    public void TheSchemaExpectations_CoverTheEmailedSecondFactorsColumns(string table, string column)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"COL_LENGTH('dbo.{table}', '{column}')")
                           && expectation.Name == $"{table}.{column} column",
            "every two-factor read selects the S24 columns, so an API ahead of its DACPAC fails every " +
            "two-factor sign-in with 'Invalid column name' (S24)");
    }

    [Fact]
    public void TheSchemaExpectations_CoverThePostLogoutRedirectUrisTable()
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains("OBJECT_ID('dbo.ApplicationPostLogoutRedirectUris', 'U')")
                           && expectation.Name.Contains("ApplicationPostLogoutRedirectUris"),
            "every single-application read loads the post-logout list (OI-151), the authorize read among " +
            "them, so an API ahead of its DACPAC fails every sign-in with 'Invalid object name'");
    }

    /// <summary>
    /// OI-58's three scope columns: an API uploaded ahead of the DACPAC fails on
    /// the first application read ("Invalid column name 'AllowedScopes'"), on the
    /// authorize and exchange paths and on every refresh.
    /// </summary>
    [Theory]
    [InlineData("Applications", "AllowedScopes")]
    [InlineData("AuthorizationCodes", "Scope")]
    [InlineData("RefreshTokens", "Scope")]
    // OI-63: every application read selects both organization-creation columns.
    [InlineData("Applications", "AllowOrganizationCreation")]
    [InlineData("Applications", "OrganizationCreatorRoleId")]
    // Every application read selects the dark-mode logo; the anonymous
    // platform branding read selects the appearance.
    [InlineData("Applications", "LogoUrlDark")]
    [InlineData("PlatformSettings", "Theme")]
    public void TheSchemaExpectations_CoverTheScopeColumns(string table, string column)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"COL_LENGTH('dbo.{table}', '{column}') IS NOT NULL")
                           && expectation.Name.Contains($"{table}.{column}"),
            "each missing half is named on its own");
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
