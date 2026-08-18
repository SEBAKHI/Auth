using Auth.Application.Configuration;

namespace Auth_API.Tests.Maintenance;

/// <summary>
/// Unit tests for <see cref="DataRetentionSettings"/> — specifically the floors,
/// which are the only thing standing between a mistyped number and a sweep that
/// deletes live rows.
/// </summary>
/// <remarks>
/// The floors live in the settings class rather than in the console's validation
/// because the console is not the only way a value arrives: appsettings, an
/// environment variable and the database override provider all bypass it. A
/// value that reaches the worker must already be safe.
/// </remarks>
public class DataRetentionSettingsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RetentionWindows_NeverFallBelowOneDay(int hostile)
    {
        // The catastrophic case: a cutoff of "now" or later sweeps rows that are
        // still in use. Zero is the value someone types meaning "don't keep any".
        var settings = new DataRetentionSettings
        {
            AuthorizationCodeDays = hostile,
            TwoFactorChallengeDays = hostile,
            PasswordResetTokenDays = hostile,
            EmailVerificationTokenDays = hostile,
            IdpSessionDays = hostile,
            RefreshTokenDays = hostile,
        };

        settings.EffectiveAuthorizationCodeDays.Should().BeGreaterThanOrEqualTo(1);
        settings.EffectiveTwoFactorChallengeDays.Should().BeGreaterThanOrEqualTo(1);
        settings.EffectivePasswordResetTokenDays.Should().BeGreaterThanOrEqualTo(1);
        settings.EffectiveEmailVerificationTokenDays.Should().BeGreaterThanOrEqualTo(1);
        settings.EffectiveIdpSessionDays.Should().BeGreaterThanOrEqualTo(1);
        settings.EffectiveRefreshTokenDays.Should().BeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(30, 90)]
    [InlineData(89, 90)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    public void RefreshTokenRetention_FloorsAtNinetyDays(int configured, int expected)
    {
        // 90 is not a round number picked for comfort. The dashboard reports
        // token revocations over a trailing window the console allows up to 90
        // days; a shorter retention deletes the rows those figures count, so the
        // numbers stay plausible while becoming wrong — the worst failure shape,
        // because nothing looks broken.
        new DataRetentionSettings { RefreshTokenDays = configured }
            .EffectiveRefreshTokenDays.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(50, 100)]
    [InlineData(4000, 4000)]
    [InlineData(50_000, 4000)]
    public void BatchSize_StaysInsideTheLockEscalationBand(int configured, int expected)
    {
        // The ceiling is not arbitrary either: SQL Server escalates row locks to
        // a table lock at roughly 5000 per statement, which would block every
        // live query against the table for the length of the batch.
        new DataRetentionSettings { BatchSize = configured }
            .EffectiveBatchSize.Should().Be(expected);
    }

    [Fact]
    public void PerRunCeiling_IsNeverSmallerThanOneBatch()
    {
        // A ceiling below the batch size would end the loop before the first
        // statement ran, and the table would never drain.
        var settings = new DataRetentionSettings { BatchSize = 4000, MaxRowsPerTablePerRun = 10 };

        settings.EffectiveMaxRowsPerTablePerRun.Should().BeGreaterThanOrEqualTo(settings.EffectiveBatchSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PollInterval_NeverBecomesZeroOrNegative(int hostile)
    {
        // Task.Delay with a negative TimeSpan throws; zero would spin the worker
        // against the database in a tight loop.
        new DataRetentionSettings { WorkerPollMinutes = hostile }
            .WorkerPollInterval.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ShippedDefaults_MatchTheDocumentedPolicy()
    {
        var settings = new DataRetentionSettings();

        settings.Enabled.Should().BeTrue();
        settings.EffectiveAuthorizationCodeDays.Should().Be(7);
        settings.EffectiveTwoFactorChallengeDays.Should().Be(7);
        settings.EffectivePasswordResetTokenDays.Should().Be(7);
        settings.EffectiveEmailVerificationTokenDays.Should().Be(7);
        settings.EffectiveIdpSessionDays.Should().Be(30);
        settings.EffectiveRefreshTokenDays.Should().Be(90);
    }
}
