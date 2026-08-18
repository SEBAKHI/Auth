using Auth.Application.Configuration;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Maintenance;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Maintenance;

/// <summary>
/// Unit tests for <see cref="ExpiredDataCleanupWorker"/> — the sweep that gave
/// seven orphaned cleanup methods a caller.
/// </summary>
public class ExpiredDataCleanupWorkerTests
{
    private readonly Mock<IAuthorizationCodeRepository> _codes = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<IPasswordResetTokenRepository> _resetTokens = new();
    private readonly Mock<IEmailVerificationTokenRepository> _verificationTokens = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IUserSessionRepository> _userSessions = new();

    private ExpiredDataCleanupWorker CreateWorker(DataRetentionSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_codes.Object);
        services.AddSingleton(_challenges.Object);
        services.AddSingleton(_resetTokens.Object);
        services.AddSingleton(_verificationTokens.Object);
        services.AddSingleton(_idpSessions.Object);
        services.AddSingleton(_refreshTokens.Object);
        services.AddSingleton(_userSessions.Object);

        return new ExpiredDataCleanupWorker(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            TestHelpers.CreateOptions(settings ?? new DataRetentionSettings()),
            new Mock<ILogger<ExpiredDataCleanupWorker>>().Object);
    }

    /// <summary>Every repository reports "nothing left" on the first batch.</summary>
    private void SetupAllDrained()
    {
        _codes.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _challenges.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _resetTokens.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _verificationTokens.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _idpSessions.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _refreshTokens.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _userSessions.Setup(r => r.MarkExpiredSessionsEndedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
    }

    [Fact]
    public async Task RunSweep_TouchesEveryTable()
    {
        // The whole point: seven cleanup methods existed and none had a caller.
        SetupAllDrained();

        await CreateWorker().RunSweepAsync(CancellationToken.None);

        _codes.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _challenges.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _resetTokens.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _verificationTokens.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _idpSessions.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _userSessions.Verify(r => r.MarkExpiredSessionsEndedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSweep_OneTableThrowing_LeavesTheOthersSwept()
    {
        // The failure that must not cascade. Authorization codes are swept first,
        // so a throw there would take the whole run down if it were not caught
        // per table — and the largest table, refresh tokens, is swept last.
        SetupAllDrained();
        _codes
            .Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("deadlock victim"));

        var act = async () => await CreateWorker().RunSweepAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _refreshTokens.Verify(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _userSessions.Verify(r => r.MarkExpiredSessionsEndedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSweep_KeepsBatchingWhileFullBatchesComeBack()
    {
        // A full batch means there may be more; a short one means the table is
        // drained. Stopping after the first batch would leave a backlog that
        // never clears.
        SetupAllDrained();
        var settings = new DataRetentionSettings { BatchSize = 100 };
        var remaining = 250;

        _refreshTokens
            .Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var take = Math.Min(100, remaining);
                remaining -= take;
                return take;
            });

        await CreateWorker(settings).RunSweepAsync(CancellationToken.None);

        // 100, 100, 50 — the short third batch ends it.
        _refreshTokens.Verify(
            r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunSweep_StopsAtThePerRunCeiling()
    {
        // Bounds the FIRST run, which faces a backlog accumulated since the
        // system was deployed. Without this it would delete for as long as it
        // takes, on a shared host, in one sitting.
        SetupAllDrained();
        var settings = new DataRetentionSettings { BatchSize = 100, MaxRowsPerTablePerRun = 300 };

        _refreshTokens
            .Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100); // never drains

        await CreateWorker(settings).RunSweepAsync(CancellationToken.None);

        _refreshTokens.Verify(
            r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunSweep_GivesEachTableItsOwnCutoff()
    {
        // Retention is per table because the evidence each one carries is worth
        // keeping for a different length of time.
        SetupAllDrained();
        var settings = new DataRetentionSettings();
        DateTime codeCutoff = default, refreshCutoff = default;

        _codes.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((c, _, _) => codeCutoff = c).ReturnsAsync(0);
        _refreshTokens.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((c, _, _) => refreshCutoff = c).ReturnsAsync(0);

        await CreateWorker(settings).RunSweepAsync(CancellationToken.None);

        codeCutoff.Should().BeCloseTo(DateTime.UtcNow.AddDays(-7), TimeSpan.FromMinutes(1));
        refreshCutoff.Should().BeCloseTo(DateTime.UtcNow.AddDays(-90), TimeSpan.FromMinutes(1));
        refreshCutoff.Should().BeBefore(codeCutoff);
    }

    [Fact]
    public async Task DailyGuard_RunsOnceThenStaysQuietForTheRestOfTheDay()
    {
        SetupAllDrained();
        var worker = CreateWorker();

        await worker.RunDailySweepIfDueAsync(CancellationToken.None);
        await worker.RunDailySweepIfDueAsync(CancellationToken.None);
        await worker.RunDailySweepIfDueAsync(CancellationToken.None);

        _codes.Verify(
            r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DailyGuard_WhenDisabled_SweepsNothing()
    {
        SetupAllDrained();

        await CreateWorker(new DataRetentionSettings { Enabled = false })
            .RunDailySweepIfDueAsync(CancellationToken.None);

        _codes.Verify(
            r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _userSessions.Verify(
            r => r.MarkExpiredSessionsEndedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSweep_ZeroRetention_StillLeavesADayOfMargin()
    {
        // End to end through the worker rather than the settings class alone:
        // whatever an operator sets, the cutoff the repository receives is never
        // the present moment, so a sweep cannot reach rows still in use.
        SetupAllDrained();
        var settings = new DataRetentionSettings { AuthorizationCodeDays = 0, IdpSessionDays = -30 };
        DateTime codeCutoff = default, idpCutoff = default;

        _codes.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((c, _, _) => codeCutoff = c).ReturnsAsync(0);
        _idpSessions.Setup(r => r.CleanupExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((c, _, _) => idpCutoff = c).ReturnsAsync(0);

        await CreateWorker(settings).RunSweepAsync(CancellationToken.None);

        codeCutoff.Should().BeBefore(DateTime.UtcNow.AddHours(-23));
        idpCutoff.Should().BeBefore(DateTime.UtcNow.AddHours(-23));
    }
}
