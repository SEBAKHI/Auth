using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// A password sign-in against an account in a chosen lock state, with the real
/// lock rules (<see cref="Auth.Application.Features.Authentication.Common.AccountLockout"/>)
/// over a repository mock. Hard′ 100, Max′ 5, 15 minutes unless a test says otherwise.
/// </summary>
internal sealed class LoginLockFixture
{
    public const string Email = "owner@example.com";

    public Mock<IUserRepository> Users { get; } = new();
    public Mock<ILoginAttemptRepository> Attempts { get; } = new();
    public Mock<IAccountDeletionRequestRepository> Deletions { get; } = new();
    public Mock<IPasswordHasher> Hasher { get; } = new();
    public Mock<ILoginResponseBuilder> Builder { get; } = new();
    public Mock<ITwoFactorChallengeService> Challenges { get; } = new();
    public PasswordSettings Settings { get; } = TestHelpers.CreatePasswordSettings();
    public List<LoginAttempt> Recorded { get; } = [];
    public LockSignalTests.CapturingLogger<Auth.Application.Features.Authentication.Common.AccountLockout> LockLog { get; } = new();

    public LoginLockFixture()
    {
        Attempts
            .Setup(r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<LoginAttempt, CancellationToken>((attempt, _) => Recorded.Add(attempt))
            .Returns(Task.CompletedTask);
        Hasher.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(false);
        Builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse { Token = new TokenResponse { AccessToken = "a", RefreshToken = "r", ExpiresIn = 900, RefreshExpiresIn = 604800 } });
    }

    public LoginCommandHandler Handler() => new(
        Users.Object,
        Attempts.Object,
        Deletions.Object,
        Hasher.Object,
        Builder.Object,
        Challenges.Object,
        new Mock<IDomainEventDispatcher>().Object,
        TestHelpers.CreateAccountLockout(Users.Object, Settings, logger: LockLog),
        TestHelpers.CreateOptions(Settings),
        NullLogger<LoginCommandHandler>.Instance);

    public static LoginCommand Command(string? ipAddress = "203.0.113.7", string? deviceId = "known-device") =>
        new(Email, "the-password", ipAddress, "Agent/1.0", deviceId);

    /// <summary>
    /// An account with these lock columns, found by the sign-in. The re-reads (after
    /// a lapse repair, and the lock columns after a correct password) see the same
    /// row unless a test changes them with <see cref="Later"/> or <see cref="Repaired"/>.
    /// </summary>
    public User Given(UserStatus status, int failedAttempts, int? expiresInMinutes)
    {
        var user = Account(status, failedAttempts, expiresInMinutes);
        Users.Setup(r => r.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        Repaired(user);
        Later(user.Id, status, failedAttempts, expiresInMinutes);
        return user;
    }

    /// <summary>The lock columns as the read after the password hash sees them.</summary>
    public void Later(Guid userId, UserStatus status, int failedAttempts, int? expiresInMinutes) =>
        Users
            .Setup(r => r.GetLockStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountLockState(
                status, failedAttempts, expiresInMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null));

    /// <summary>The account as the read after a lapse repair sees it.</summary>
    public void Repaired(User user) =>
        Users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

    /// <summary>The same account with other lock columns (a later read of the row).</summary>
    public static User Account(UserStatus status, int failedAttempts, int? expiresInMinutes, Guid? id = null) =>
        TestHelpers.CreateUser(
            id: id,
            email: Email,
            status: status,
            failedLoginAttempts: failedAttempts,
            lockoutEnd: expiresInMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);

    public void Familiar(bool familiar) =>
        Attempts
            .Setup(r => r.HasSucceededFromAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(familiar);

    public void PasswordIs(bool right) =>
        Hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(right);

    public void VerifyPasswordChecked(Times times) =>
        Hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), times);

    public void VerifyBuilt(bool passwordProven, Times times) =>
        Builder.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), passwordProven, It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()),
            times);

    public void VerifyNothingBuilt() =>
        Builder.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()),
            Times.Never);
}

/// <summary>
/// X12, LK3.2: what a refused sign-in is told. A timed lock says when it ends; the
/// hard lock names the reset that lifts it (waiting never will); an administrator's
/// lock answers as it always did; the per-address ceiling stays undated. None of
/// them spends a password hash.
/// </summary>
/// <remarks>
/// The provider side (a hard lock refuses an external sign-in with the same code,
/// even from a familiar device) is ExternalLoginCommandHandlerTests
/// .Handle_HardLock_FamiliarDevice_IsRefusedWithTheResetItNeeds. Deliberate break:
/// answer the hard lock with <c>AccountLockedUntil(null)</c> (that is
/// <c>User.AccountLocked</c>) in either handler.
/// </remarks>
public class LockResponseSurfaceTests
{
    private readonly LoginLockFixture _fx = new();

    [Fact]
    public async Task TimedLock_Stranger_IsToldWhenItEnds()
    {
        var user = _fx.Given(UserStatus.Locked, 5, 15);
        _fx.Familiar(false);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
        result.FirstError.Metadata!["args"].Should().BeEquivalentTo(new object[] { user.LockoutEnd!.Value.ToString("u") });
        _fx.VerifyPasswordChecked(Times.Never());
    }

    [Fact]
    public async Task HardLock_Stranger_IsToldToResetThePassword()
    {
        _fx.Given(UserStatus.Locked, 100, null);
        _fx.Familiar(false);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedResetRequired");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Forbidden);
        _fx.VerifyPasswordChecked(Times.Never());
        _fx.Recorded.Should().ContainSingle(a => a.FailureReason == LoginFailureReasons.AccountLocked);
    }

    [Fact]
    public async Task HardLock_FamiliarPastTheSeal_IsToldToResetThePassword()
    {
        _fx.Given(UserStatus.Locked, 105, null);
        _fx.Familiar(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedResetRequired");
        _fx.VerifyPasswordChecked(Times.Never());
    }

    [Theory]
    [InlineData(null, "User.AccountLocked")]
    [InlineData(60, "User.AccountLockedUntil")]
    public async Task AdministratorsLock_AnswersAsBefore_EvenToAFamiliarSource(int? expiresInMinutes, string code)
    {
        _fx.Given(UserStatus.Locked, 0, expiresInMinutes);
        _fx.Familiar(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _fx.VerifyPasswordChecked(Times.Never());
    }

    [Fact]
    public async Task CorrectGuess_WhileABurstLockedTheAccount_IsRefusedLikeItsSiblings()
    {
        // Read active; while this request hashed, its siblings' wrong passwords
        // locked the row. A stranger's correct guess must not sign in, nor lift
        // the lock they set (security review, G9).
        var user = _fx.Given(UserStatus.Active, 4, null);
        _fx.Later(user.Id, UserStatus.Locked, 5, 15);
        _fx.Familiar(false);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
        _fx.VerifyNothingBuilt();
    }

    [Fact]
    public async Task CorrectGuess_WhileABurstLockedTheAccount_StillSignsInAFamiliarSource()
    {
        var user = _fx.Given(UserStatus.Active, 4, null);
        _fx.Later(user.Id, UserStatus.Locked, 5, 15);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _fx.VerifyBuilt(passwordProven: true, Times.Once());
    }

    [Fact]
    public async Task FamiliarSource_RefusedByTheCeiling_IsNotReportedAsAdmitted()
    {
        var user = _fx.Given(UserStatus.Locked, 100, null);
        _fx.Familiar(true);
        _fx.Attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(user.Id, "203.0.113.7", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLocked");
        _fx.LockLog.Entries.Should().NotContain(e => (string?)e.Values["EventCode"] == AccountLockSignalCodes.FamiliarAdmitted);
    }

    [Fact]
    public async Task PerAddressCeiling_StaysUndated()
    {
        _fx.Given(UserStatus.Active, 0, null);
        _fx.Attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(It.IsAny<Guid>(), "203.0.113.7", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLocked");
        _fx.VerifyPasswordChecked(Times.Never());
    }
}

/// <summary>
/// X12, review 64 F1: the lock and the per-address ceiling are read before the
/// password hash, and a concurrent burst all reads the same row. So after a correct
/// password both are judged again, for EVERY request, on the row as it is now: a
/// correct guess inside a burst that sealed the familiar door, used up the address's
/// ceiling, or met an administrator's lock or a deactivation committed during the
/// hash, neither signs in nor lifts the lock.
/// </summary>
/// <remarks>
/// Deliberate breaks: judge again only when the first read found no lock (the
/// review round's <c>AdmittedUnder == None</c> guard) — the sealed-door and the
/// administrator rows turn red; drop the familiar re-count — the ceiling row turns red.
/// </remarks>
public class LockJudgedAfterThePasswordTests
{
    private readonly LoginLockFixture _fx = new();

    [Fact]
    public async Task CorrectGuess_FamiliarUnderHard_AfterABurstSealedTheDoor_IsRefused()
    {
        var user = _fx.Given(UserStatus.Locked, 102, null);
        _fx.Later(user.Id, UserStatus.Locked, 110, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedResetRequired");
        _fx.VerifyNothingBuilt();
    }

    [Fact]
    public async Task CorrectGuess_FamiliarUnderHard_AfterABurstUsedTheAddressCeiling_IsRefused()
    {
        var user = _fx.Given(UserStatus.Locked, 100, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);
        _fx.Attempts
            .SetupSequence(r => r.CountFailedAttemptsForUserFromIpAsync(user.Id, "203.0.113.7", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .ReturnsAsync(5);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLocked");
        (result.FirstError.Metadata ?? []).Should().NotContainKey(
            "args", "the address's window end is not known here, so the answer is undated");
        _fx.VerifyNothingBuilt();
        _fx.Recorded.Should().ContainSingle(a => a.FailureReason == LoginFailureReasons.SourceLocked);
    }

    [Theory]
    [InlineData(UserStatus.Locked, 0, "User.AccountLocked")]
    [InlineData(UserStatus.Inactive, 100, "User.AccountInactive")]
    public async Task CorrectGuess_Familiar_AdministratorActedDuringTheHash_IsRefused(
        UserStatus statusNow, int countNow, string code)
    {
        // Admitted as familiar under the hard lock; while it hashed, an
        // administrator locked the account (count 0, no expiry) or deactivated it.
        var user = _fx.Given(UserStatus.Locked, 100, null);
        _fx.Later(user.Id, statusNow, countNow, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _fx.VerifyNothingBuilt();
    }

    [Fact]
    public async Task CorrectGuess_AccountDeletedDuringTheHash_IsRefused()
    {
        var user = _fx.Given(UserStatus.Active, 0, null);
        _fx.Users
            .Setup(r => r.GetLockStateAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountLockState?)null);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _fx.VerifyNothingBuilt();
    }

    [Fact]
    public async Task CorrectGuess_FamiliarBelowTheSealAndTheCeiling_StillSignsIn()
    {
        // The door still opens when the burst has neither sealed it nor used up
        // this address's ceiling.
        var user = _fx.Given(UserStatus.Locked, 100, null);
        _fx.Later(user.Id, UserStatus.Locked, 103, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);
        _fx.Attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(user.Id, "203.0.113.7", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _fx.VerifyBuilt(passwordProven: true, Times.Once());
    }
}

/// <summary>
/// X12, LK4 exit 2 (owner decision: the familiar door stays): an address or device
/// the owner used recently may still prove the password under a timed or a hard
/// lock, until the episode passes the hard limit by a full threshold (the seal,
/// Hard′ + Max′ = 105). The success lifts the lock; admission alone lifts nothing.
/// </summary>
/// <remarks>Deliberate break: drop the seal check from the login handler; the 105 row turns red.</remarks>
public class LockExitFamiliarTests
{
    private readonly LoginLockFixture _fx = new();

    [Theory]
    [InlineData(100)]
    [InlineData(104)]
    public async Task HardLock_FamiliarBeforeTheSeal_SignsIn_AndTheSuccessLiftsTheLock(int count)
    {
        _fx.Given(UserStatus.Locked, count, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _fx.VerifyBuilt(passwordProven: true, Times.Once());
        _fx.Users.Verify(r => r.ClearAutomaticLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task HardLock_FamiliarAtTheSeal_IsRefused()
    {
        _fx.Given(UserStatus.Locked, 105, null);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _fx.VerifyNothingBuilt();
    }

    [Fact]
    public async Task HardLock_FamiliarWrongPassword_KeepsCounting()
    {
        var user = _fx.Given(UserStatus.Locked, 100, null);
        _fx.Familiar(true);
        _fx.PasswordIs(false);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _fx.Users.Verify(
            r => r.RecordFailedLoginAsync(user.Id, 100, 5, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task TimedLock_FamiliarSource_SignsIn()
    {
        _fx.Given(UserStatus.Locked, 5, 15);
        _fx.Familiar(true);
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _fx.VerifyBuilt(passwordProven: true, Times.Once());
    }
}

/// <summary>
/// X12, LK2.4: a lock whose expiry passed is repaired before the sign-in is judged,
/// and the count is KEPT (under Enforce). Waiting out a lock used to wipe the count,
/// so it never passed five, and a guesser had about 480 tries a day for ever.
/// </summary>
/// <remarks>Deliberate break: repair with the count reset under Enforce.</remarks>
public class LoginLapsedLockRepairTests
{
    private readonly LoginLockFixture _fx = new();

    [Fact]
    public async Task LapsedLock_IsRepairedKeepingTheCount_ThenThePasswordIsChecked()
    {
        var user = _fx.Given(UserStatus.Locked, 5, -1);
        _fx.PasswordIs(false);

        await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        _fx.Users.Verify(r => r.RepairLapsedLockAsync(user.Id, false, It.IsAny<CancellationToken>()), Times.Once());
        _fx.Users.Verify(r => r.RepairLapsedLockAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Never());
        _fx.VerifyPasswordChecked(Times.Once());
        _fx.Users.Verify(
            r => r.RecordFailedLoginAsync(user.Id, 100, 5, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task LapsedLock_LockedAgainBeforeTheRepair_IsJudgedAsItIsNow()
    {
        // The repair found the row locked again by a concurrent failure (it
        // matched nothing); the re-read shows the timed lock, and a stranger is
        // refused before the hash.
        var lapsed = _fx.Given(UserStatus.Locked, 5, -1);
        _fx.Repaired(LoginLockFixture.Account(UserStatus.Locked, 10, 15, lapsed.Id));
        _fx.Familiar(false);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
        _fx.VerifyPasswordChecked(Times.Never());
    }

    [Fact]
    public async Task UnlockedAccount_IsNotRepaired()
    {
        _fx.Given(UserStatus.Active, 3, null);
        _fx.PasswordIs(true);

        await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        _fx.Users.Verify(r => r.RepairLapsedLockAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

/// <summary>
/// X12, LK6.1–6.3: an account pending deletion is never locked (a lock its owner
/// could not lift by signing in would outlast the grace period). Guessing is bounded
/// per address instead, before the hash runs, and every wrong password is recorded
/// against the account so the ceiling counts it; the answer stays generic.
/// </summary>
/// <remarks>
/// Deliberate break: count the pending-deletion branch's failures against the
/// account (call the failure statement); the never-counted assertion turns red.
/// </remarks>
public class PendingDeletionNoLockTests
{
    private readonly LoginLockFixture _fx = new();
    private User _deleted = null!;

    private void GivenPendingDeletion()
    {
        _deleted = TestHelpers.CreateUser(email: LoginLockFixture.Email, isDeleted: true, deletedAt: DateTime.UtcNow);
        _fx.Users.Setup(r => r.GetByEmailAsync(LoginLockFixture.Email, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _fx.Users.Setup(r => r.GetByEmailIncludeDeletedAsync(LoginLockFixture.Email, It.IsAny<CancellationToken>())).ReturnsAsync(_deleted);
        _fx.Deletions
            .Setup(r => r.GetActiveByUserIdAsync(_deleted.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(
                _deleted.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", _deleted.Id));
    }

    [Fact]
    public async Task WrongPassword_IsRecordedAgainstTheAccount_AndNeverCounted()
    {
        GivenPendingDeletion();
        _fx.PasswordIs(false);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _fx.Recorded.Should().ContainSingle(a => a.UserId == _deleted.Id && a.FailureReason == LoginFailureReasons.InvalidPassword);
        _fx.Users.Verify(
            r => r.RecordFailedLoginAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task CeilingReached_RefusesBeforeTheHash_WithTheGenericAnswer()
    {
        GivenPendingDeletion();
        _fx.Attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(_deleted.Id, "203.0.113.7", TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _fx.VerifyPasswordChecked(Times.Never());
        _fx.Recorded.Should().ContainSingle(a => a.UserId == _deleted.Id && a.FailureReason == LoginFailureReasons.SourceLocked);
    }

    [Fact]
    public async Task RightPassword_StillShowsTheRecoveryPath()
    {
        GivenPendingDeletion();
        _fx.PasswordIs(true);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.AccountPendingDeletion");
    }

    [Fact]
    public async Task RightPassword_CeilingReachedDuringTheHash_KeepsTheGenericAnswer()
    {
        // Review 64 F2: a burst from one address all passed the ceiling together;
        // the correct guess among them is counted again after the hash.
        GivenPendingDeletion();
        _fx.PasswordIs(true);
        _fx.Attempts
            .SetupSequence(r => r.CountFailedAttemptsForUserFromIpAsync(_deleted.Id, "203.0.113.7", TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4)
            .ReturnsAsync(5);

        var result = await _fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _fx.Recorded.Should().ContainSingle(a => a.UserId == _deleted.Id && a.FailureReason == LoginFailureReasons.SourceLocked);
    }
}

/// <summary>
/// X12, LK9.2: Legacy (the hot rollback for the first sixteen days) changes exactly
/// three things: no new hard lock, a lapse restarts the count, and no per-address
/// ceiling on accounts pending deletion. An existing hard lock keeps its exits.
/// </summary>
/// <remarks>Deliberate break: keep passing the hard limit under Legacy.</remarks>
public class ConsecutiveFailureModeTests
{
    [Theory]
    [InlineData(ConsecutiveFailureMode.Enforce, 100)]
    [InlineData(ConsecutiveFailureMode.Legacy, null)]
    public async Task WrongPassword_PassesTheHardLimitOnlyUnderEnforce(ConsecutiveFailureMode mode, int? hard)
    {
        var fx = new LoginLockFixture();
        fx.Settings.ConsecutiveFailureMode = mode;
        var user = fx.Given(UserStatus.Active, 0, null);
        fx.PasswordIs(false);

        await fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        fx.Users.Verify(r => r.RecordFailedLoginAsync(user.Id, hard, 5, TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData(ConsecutiveFailureMode.Enforce, false)]
    [InlineData(ConsecutiveFailureMode.Legacy, true)]
    public async Task LapseRepair_RestartsTheCountOnlyUnderLegacy(ConsecutiveFailureMode mode, bool reset)
    {
        var fx = new LoginLockFixture();
        fx.Settings.ConsecutiveFailureMode = mode;
        var user = fx.Given(UserStatus.Locked, 5, -1);
        fx.PasswordIs(true);

        await fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        fx.Users.Verify(r => r.RepairLapsedLockAsync(user.Id, reset, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Legacy_DropsThePendingDeletionCeiling()
    {
        var fx = new LoginLockFixture();
        fx.Settings.ConsecutiveFailureMode = ConsecutiveFailureMode.Legacy;
        var deleted = TestHelpers.CreateUser(email: LoginLockFixture.Email, isDeleted: true, deletedAt: DateTime.UtcNow);
        fx.Users.Setup(r => r.GetByEmailIncludeDeletedAsync(LoginLockFixture.Email, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        fx.Deletions
            .Setup(r => r.GetActiveByUserIdAsync(deleted.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(deleted.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", deleted.Id));
        fx.PasswordIs(false);

        await fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        fx.Attempts.Verify(
            r => r.CountFailedAttemptsForUserFromIpAsync(deleted.Id, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Legacy_LeavesAnExistingHardLockWithItsExits()
    {
        // Strangers are still told to reset; a familiar source still gets in.
        var fx = new LoginLockFixture();
        fx.Settings.ConsecutiveFailureMode = ConsecutiveFailureMode.Legacy;
        fx.Given(UserStatus.Locked, 100, null);
        fx.Familiar(false);

        var refused = await fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        refused.FirstError.Code.Should().Be("User.AccountLockedResetRequired");

        fx.Familiar(true);
        fx.PasswordIs(true);
        var admitted = await fx.Handler().Handle(LoginLockFixture.Command(), CancellationToken.None);

        admitted.IsError.Should().BeFalse();
        fx.VerifyBuilt(passwordProven: true, Times.Once());
    }
}
