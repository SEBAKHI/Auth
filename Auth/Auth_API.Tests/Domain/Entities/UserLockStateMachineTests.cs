using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// X12, LK1.5: the one classifier of the lock columns, and the transitions the
/// account itself decides. Every sign-in, renewal and recovery path asks
/// <see cref="AccountLockState.Kind"/>; an administrator's lock is told from an
/// automatic one by the count alone, never by a missing expiry, because the hard
/// lock has no expiry either.
/// </summary>
/// <remarks>
/// Deliberate break: classify "automatic" as a count at or past the timed-lock
/// threshold (the old <c>IsLockedByFailedAttempts</c> rule). A timed lock whose
/// count is below the threshold — the owner raised MaxFailedAttempts while it was
/// live, or a familiar source's failure kept counting inside a window — then reads
/// as an administrator's lock, and the rows below turn red.
/// </remarks>
public class UserLockStateMachineTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<UserStatus, int, int?, AccountLockKind> Table => new()
    {
        // status, count, expiry in minutes from now (null = none), kind
        { UserStatus.Active, 0, null, AccountLockKind.None },
        { UserStatus.Active, 7, null, AccountLockKind.None },
        { UserStatus.Inactive, 100, null, AccountLockKind.None },
        { UserStatus.Pending, 0, null, AccountLockKind.None },
        { UserStatus.Locked, 0, null, AccountLockKind.Admin },
        { UserStatus.Locked, 0, 60 * 24, AccountLockKind.Admin },
        { UserStatus.Locked, 5, 15, AccountLockKind.Timed },
        { UserStatus.Locked, 3, 15, AccountLockKind.Timed },
        { UserStatus.Locked, 1, 15, AccountLockKind.Timed },
        { UserStatus.Locked, 100, null, AccountLockKind.Hard },
        { UserStatus.Locked, 1, null, AccountLockKind.Hard },
        { UserStatus.Locked, 5, -1, AccountLockKind.Lapsed },
        { UserStatus.Locked, 0, -1, AccountLockKind.Lapsed },
    };

    [Fact]
    public void Table_IsNotEmpty() => Table.Count.Should().BeGreaterThan(10);

    [Theory]
    [MemberData(nameof(Table))]
    public void Kind_ReadsTheThreeColumns(UserStatus status, int count, int? expiresInMinutes, AccountLockKind expected)
    {
        var end = expiresInMinutes is { } minutes ? Now.AddMinutes(minutes) : (DateTime?)null;

        new AccountLockState(status, count, end).Kind(Now).Should().Be(expected);
    }

    [Fact]
    public void Kind_AtTheExactExpiry_IsLapsed()
    {
        new AccountLockState(UserStatus.Locked, 5, Now).Kind(Now).Should().Be(AccountLockKind.Lapsed);
    }

    [Fact]
    public void Default_IsNotLocked()
    {
        default(AccountLockState).Kind(Now).Should().Be(AccountLockKind.None);
    }

    [Fact]
    public void IsLiveAndIsAutomatic_FollowTheKind()
    {
        var hard = new AccountLockState(UserStatus.Locked, 100, null);
        var admin = new AccountLockState(UserStatus.Locked, 0, null);
        var lapsed = new AccountLockState(UserStatus.Locked, 5, Now.AddMinutes(-1));

        hard.IsLive(Now).Should().BeTrue();
        hard.IsAutomatic(Now).Should().BeTrue();
        admin.IsLive(Now).Should().BeTrue();
        admin.IsAutomatic(Now).Should().BeFalse();
        lapsed.IsLive(Now).Should().BeFalse();
        lapsed.IsAutomatic(Now).Should().BeFalse();
    }

    [Fact]
    public void Lock_StartsAnAdministratorEpisodeAtZero_WithOrWithoutAnExpiry()
    {
        var user = TestHelpers.CreateUser(status: UserStatus.Active, failedLoginAttempts: 99);

        var change = user.Lock(DateTime.UtcNow.AddHours(24), Guid.NewGuid());

        user.FailedLoginAttempts.Should().Be(0);
        user.LockKind(DateTime.UtcNow).Should().Be(AccountLockKind.Admin);
        change.Status.Should().Be(UserStatus.Locked);
        change.ResetsLock.Should().BeTrue();

        user.Lock(null, Guid.NewGuid());
        user.LockKind(DateTime.UtcNow).Should().Be(AccountLockKind.Admin);
    }

    [Fact]
    public void Unlock_ClearsAnyLockAndTheCount()
    {
        var user = TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: 100);

        var change = user.Unlock(Guid.NewGuid());

        user.LockKind(DateTime.UtcNow).Should().Be(AccountLockKind.None);
        user.FailedLoginAttempts.Should().Be(0);
        change.Should().Be(AccountStatusChange.Unlock());
    }

    [Fact]
    public void Deactivate_LeavesTheCountAndTheExpiry()
    {
        var end = DateTime.UtcNow.AddMinutes(10);
        var user = TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: 7, lockoutEnd: end);

        var change = user.Deactivate(Guid.NewGuid());

        user.Status.Should().Be(UserStatus.Inactive);
        user.FailedLoginAttempts.Should().Be(7);
        user.LockoutEnd.Should().Be(end);
        change.ResetsLock.Should().BeFalse();
    }

    [Theory]
    [InlineData(104, false)]
    [InlineData(105, true)]
    [InlineData(150, true)]
    public void FamiliarDoor_IsSealedOnceTheCountPassesTheLimitByAThreshold(int count, bool sealedExpected)
    {
        var user = TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: count);

        user.IsFamiliarDoorSealed(hardLimit: 100, lockThreshold: 5).Should().Be(sealedExpected);
    }

    [Fact]
    public void LockRefusal_NamesTheWayOut()
    {
        var end = DateTime.UtcNow.AddMinutes(15);
        TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: 100)
            .LockRefusal(AccountLockKind.Hard).Code.Should().Be("User.AccountLockedResetRequired");
        TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: 5, lockoutEnd: end)
            .LockRefusal(AccountLockKind.Timed).Code.Should().Be("User.AccountLockedUntil");
        TestHelpers.CreateUser(status: UserStatus.Locked)
            .LockRefusal(AccountLockKind.Admin).Code.Should().Be("User.AccountLocked");
    }

    [Fact]
    public void Transition_NamesWhatTheStatementDid()
    {
        var active = new AccountLockState(UserStatus.Active, 4, null);
        var timed = new AccountLockState(UserStatus.Locked, 5, Now.AddMinutes(15));
        var retimed = new AccountLockState(UserStatus.Locked, 10, Now.AddMinutes(30));
        var hard = new AccountLockState(UserStatus.Locked, 100, null);
        var lapsed = new AccountLockState(UserStatus.Locked, 5, Now.AddMinutes(-1));
        var cleared = new AccountLockState(UserStatus.Active, 0, null);
        var id = Guid.NewGuid();

        new AccountLockTransition(id, active, timed).StartedTimedLock(Now).Should().BeTrue();
        new AccountLockTransition(id, timed, retimed).StartedTimedLock(Now).Should().BeTrue("a new expiry is a new lock");
        new AccountLockTransition(id, timed, new AccountLockState(UserStatus.Locked, 6, timed.LockoutEndUtc))
            .StartedTimedLock(Now).Should().BeFalse("a failure inside the window that keeps its end locks nothing new");
        new AccountLockTransition(id, timed, hard).StartedHardLock(Now).Should().BeTrue();
        new AccountLockTransition(id, hard, new AccountLockState(UserStatus.Locked, 101, null))
            .StartedHardLock(Now).Should().BeFalse("already hard");
        new AccountLockTransition(id, hard, cleared).LiftedAutomaticLock(Now).Should().BeTrue();
        new AccountLockTransition(id, timed, cleared).LiftedAutomaticLock(Now).Should().BeTrue();
        new AccountLockTransition(id, lapsed, new AccountLockState(UserStatus.Active, 5, null))
            .LiftedAutomaticLock(Now).Should().BeFalse("a lapsed lock had already ended by itself");
        new AccountLockTransition(id, new AccountLockState(UserStatus.Locked, 0, null), new AccountLockState(UserStatus.Locked, 0, null))
            .LiftedAutomaticLock(Now).Should().BeFalse("an administrator's lock is never lifted by these statements");
    }
}
