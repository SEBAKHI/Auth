using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, LK8.2 and LK8.5: one log line per transition of the lock columns, named by
/// its event code and carrying the account, the count and the expiry — never the
/// email address, the password, the client address, the device or the agent — and,
/// when the hard limit is reached, <see cref="UserHardLockedEvent"/> published after
/// the write and not cancellable by the caller.
/// </summary>
/// <remarks>
/// Deliberate break: write an <c>account.unlocked</c> line for a lapse repair. A
/// lapsed lock had already ended by itself; a line there would make the day-16 count
/// of unlocks meaningless.
/// </remarks>
public class LockSignalTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly CapturingLogger<AccountLockout> _log = new();
    private readonly Guid _userId = Guid.NewGuid();

    private Auth.Application.Interfaces.IAccountLockout Lockout() =>
        TestHelpers.CreateAccountLockout(_users.Object, publisher: _publisher.Object, logger: _log);

    private static AccountLockTransition Transition(Guid id, AccountLockState before, AccountLockState after) => new(id, before, after);

    private void FailureReturns(AccountLockState before, AccountLockState after) =>
        _users
            .Setup(r => r.RecordFailedLoginAsync(_userId, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transition(_userId, before, after));

    [Fact]
    public async Task TimedLock_WritesOneLockedLine()
    {
        var end = DateTime.UtcNow.AddMinutes(15);
        FailureReturns(new(UserStatus.Active, 4, null), new(UserStatus.Locked, 5, end));

        await Lockout().RecordPasswordFailureAsync(_userId, CancellationToken.None);

        var line = _log.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Values["EventCode"].Should().Be(AccountLockSignalCodes.Locked);
        line.Values["UserId"].Should().Be(_userId);
        line.Values["FailedAttempts"].Should().Be(5);
        line.Values["LockoutEndUtc"].Should().Be(end);
        _publisher.Verify(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HardLock_WritesOneHardLockedLine_AndPublishesTheEventAfterTheWrite()
    {
        FailureReturns(new(UserStatus.Active, 99, null), new(UserStatus.Locked, 100, null));
        UserHardLockedEvent? published = null;
        CancellationToken? token = null;
        _publisher
            .Setup(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((e, ct) => { published = (UserHardLockedEvent)e; token = ct; })
            .Returns(Task.CompletedTask);
        using var callerToken = new CancellationTokenSource();

        await Lockout().RecordPasswordFailureAsync(_userId, callerToken.Token);

        var line = _log.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Values["EventCode"].Should().Be(AccountLockSignalCodes.HardLocked);
        line.Values["FailedAttempts"].Should().Be(100);

        published.Should().NotBeNull();
        published!.EventId.Should().NotBeEmpty();
        published.UserId.Should().Be(_userId);
        published.Factor.Should().Be(HardLockedFactor.Password);
        published.FailedAttempts.Should().Be(100);
        published.TriggeredBy.Should().BeNull("wrong guesses identify nobody");
        published.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        token.Should().Be(CancellationToken.None, "the lock is committed; its audit row must not depend on the client waiting");
    }

    [Fact]
    public async Task FailureThatLocksNothingNew_WritesNoLine()
    {
        var end = DateTime.UtcNow.AddMinutes(10);
        FailureReturns(new(UserStatus.Locked, 6, end), new(UserStatus.Locked, 7, end));

        await Lockout().RecordPasswordFailureAsync(_userId, CancellationToken.None);

        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task NothingCounted_WritesNoLine()
    {
        _users
            .Setup(r => r.RecordFailedLoginAsync(_userId, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountLockTransition?)null);

        await Lockout().RecordPasswordFailureAsync(_userId, CancellationToken.None);

        _log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(100, null, LogLevel.Warning)]
    [InlineData(5, 15, LogLevel.Information)]
    public async Task Clear_WritesOneUnlockedLine_WarningForAHardLock(int count, int? minutes, LogLevel level)
    {
        var before = new AccountLockState(UserStatus.Locked, count, minutes is { } m ? DateTime.UtcNow.AddMinutes(m) : null);
        _users
            .Setup(r => r.ClearAutomaticLockAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transition(_userId, before, new(UserStatus.Active, 0, null)));

        await Lockout().ClearAutomaticLockAsync(_userId, CancellationToken.None);

        var line = _log.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(level);
        line.Values["EventCode"].Should().Be(AccountLockSignalCodes.Unlocked);
    }

    [Fact]
    public async Task SignInThatLiftedTheLock_WritesOneUnlockedLine()
    {
        _users
            .Setup(r => r.RecordSuccessfulLoginAsync(_userId, It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transition(_userId, new(UserStatus.Locked, 100, null), new(UserStatus.Active, 0, null)));

        await Lockout().RecordSignInAsync(_userId, "203.0.113.7", passwordProven: true, CancellationToken.None);

        _log.Entries.Should().ContainSingle(e => (string)e.Values["EventCode"]! == AccountLockSignalCodes.Unlocked);
    }

    [Fact]
    public async Task LapseRepair_WritesNoLine()
    {
        _users
            .Setup(r => r.RepairLapsedLockAsync(_userId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transition(_userId, new(UserStatus.Locked, 5, DateTime.UtcNow.AddMinutes(-1)), new(UserStatus.Active, 5, null)));

        await Lockout().RepairLapsedLockAsync(_userId, CancellationToken.None);

        _log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountLockKind.Hard, LogLevel.Warning)]
    [InlineData(AccountLockKind.Timed, LogLevel.Information)]
    public void FamiliarAdmission_WritesOneLine_WarningUnderAHardLock(AccountLockKind kind, LogLevel level)
    {
        Lockout().ReportFamiliarAdmission(_userId, kind, 100);

        var line = _log.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(level);
        line.Values["EventCode"].Should().Be(AccountLockSignalCodes.FamiliarAdmitted);
    }

    [Fact]
    public async Task EveryLine_CarriesOnlyTheAccountTheCountAndTheExpiry()
    {
        var allowed = new HashSet<string> { "EventCode", "UserId", "FailedAttempts", "LockoutEndUtc", "LockKind", "Exit", "{OriginalFormat}" };
        FailureReturns(new(UserStatus.Active, 99, null), new(UserStatus.Locked, 100, null));
        _users
            .Setup(r => r.ClearAutomaticLockAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transition(_userId, new(UserStatus.Locked, 100, null), new(UserStatus.Active, 0, null)));
        var lockout = Lockout();

        await lockout.RecordPasswordFailureAsync(_userId, CancellationToken.None);
        await lockout.ClearAutomaticLockAsync(_userId, CancellationToken.None);
        lockout.ReportFamiliarAdmission(_userId, AccountLockKind.Hard, 100);

        _log.Entries.Should().HaveCount(3);
        _log.Entries.Should().AllSatisfy(e =>
        {
            e.Values.Keys.Should().BeSubsetOf(allowed, "no email, password, address, device or agent in a lock line");
            e.Message.Should().NotContain("@");
        });
    }

    // ── X12 PR B: the second factor's cap (LK8.1, LK8.2, LK8.5) ────────────────

    private readonly CapturingLogger<SecondFactorLockout> _secondFactorLog = new();

    private Auth.Application.Interfaces.ISecondFactorLockout SecondFactorLockout(
        Auth.Application.Configuration.ConsecutiveFailureMode mode = Auth.Application.Configuration.ConsecutiveFailureMode.Enforce)
    {
        var settings = TestHelpers.CreatePasswordSettings();
        settings.ConsecutiveFailureMode = mode;
        return TestHelpers.CreateSecondFactorLockout(settings, _publisher.Object, _secondFactorLog);
    }

    [Fact]
    public async Task SecondFactor_ReservationThatReachesTheLimit_WritesOneHardLockedLine_AndPublishesTheEvent()
    {
        UserHardLockedEvent? published = null;
        CancellationToken? token = null;
        _publisher
            .Setup(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((e, ct) => { published = (UserHardLockedEvent)e; token = ct; })
            .Returns(Task.CompletedTask);

        await SecondFactorLockout().ReportReservationAsync(_userId, failedAttempts: 100);

        var line = _secondFactorLog.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Values["EventCode"].Should().Be(AccountLockSignalCodes.TwoFactorHardLocked);
        line.Values["EventCode"].Should().Be("two-factor.hard-locked");
        line.Values["UserId"].Should().Be(_userId);
        line.Values["FailedAttempts"].Should().Be(100);
        line.Values.Keys.Should().BeSubsetOf(["EventCode", "UserId", "FailedAttempts", "{OriginalFormat}"],
            "no email, code, address, device or agent in a lock line");
        line.Message.Should().NotContain("@");

        published.Should().NotBeNull();
        published!.EventId.Should().NotBeEmpty();
        published.UserId.Should().Be(_userId);
        published.Factor.Should().Be(HardLockedFactor.SecondFactor);
        published.FailedAttempts.Should().Be(100);
        published.TriggeredBy.Should().BeNull("wrong codes identify nobody");
        published.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        token.Should().Be(CancellationToken.None, "the count is committed; its notices must not depend on the client waiting");
    }

    [Fact]
    public async Task SecondFactor_APublishThatThrows_IsLoggedAtError_AndNeverReachesTheCaller()
    {
        // The report runs before the code is checked: an exception escaping it would
        // answer a correct 100th code with an error. Deliberate break: drop the catch.
        _publisher
            .Setup(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("a handler could not be built"));

        var act = () => SecondFactorLockout().ReportReservationAsync(_userId, failedAttempts: 100);

        await act.Should().NotThrowAsync();
        _secondFactorLog.Entries.Select(e => e.Level).Should().Equal([LogLevel.Warning, LogLevel.Error],
            "the lock line first, then the failed publish");
        var failure = _secondFactorLog.Entries[1];
        failure.Values["EventCode"].Should().Be(AccountLockSignalCodes.TwoFactorHardLocked);
        failure.Values["UserId"].Should().Be(_userId);
        failure.Message.Should().NotContain("@");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(99)]
    public async Task SecondFactor_ReservationBelowTheLimit_WritesNothing(int failedAttempts)
    {
        await SecondFactorLockout().ReportReservationAsync(_userId, failedAttempts);

        _secondFactorLog.Entries.Should().BeEmpty("only the reservation that reaches the limit is a transition");
        _publisher.Verify(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task SecondFactor_UnderLegacy_ThereIsNoLimit_AndNoTransition()
    {
        var lockout = SecondFactorLockout(Auth.Application.Configuration.ConsecutiveFailureMode.Legacy);

        await lockout.ReportReservationAsync(_userId, failedAttempts: 100);

        lockout.HardLimit.Should().BeNull();
        _secondFactorLog.Entries.Should().BeEmpty();
        _publisher.Verify(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>A logger that keeps each entry's level, message and structured values.</summary>
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Values)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : [];
            Entries.Add((logLevel, formatter(state, exception), values));
        }
    }
}
