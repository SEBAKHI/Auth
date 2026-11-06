using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B (FA6, ARR-1043, ARR-1361, owner decision 2026-09-28): a lock and an account's state
/// at the passkey sign-in. A lock raised by wrong passwords — timed or hard — admits the passkey
/// and lifts nothing: the status, the failure count and the expiry stay as they were, the session
/// does not prove the password, and a Warning names the lock's kind. An administrator's lock
/// refuses with the answer the password sign-in gives. A deleted account is no sign-in: one
/// waiting for deletion learns when its grace period ends, any other is a failed ceremony.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the hard lock refused (the owner's lock-out by a stranger's
/// wrong passwords); the lock lifted (<c>passwordProven: true</c>); a deleted account read with the
/// deleted ones (it signed in).
/// </remarks>
public class PasskeyLockAdmissionTests
{
    public static TheoryData<string, int, double?> AutomaticLocks => new()
    {
        // Kind, failure count, minutes until the lock ends (null: no end).
        { "Timed", 5, 15 },
        { "Hard", 100, null },
    };

    [Theory]
    [MemberData(nameof(AutomaticLocks))]
    public async Task ALockRaisedByWrongPasswords_AdmitsThePasskey_AndLiftsNothing(string kind, int failures, double? minutesLeft)
    {
        var end = minutesLeft is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : (DateTime?)null;
        using var fixture = new PasskeySignInFixture(status: UserStatus.Locked, failedLoginAttempts: failures, lockoutEnd: end);
        fixture.Owner.LockKind(DateTime.UtcNow).ToString().Should().Be(kind, "the fixture holds the lock the row says");

        var result = await fixture.SignInAsync();

        result.IsError.Should().BeFalse($"{kind}: the passkey is admitted (ARR-1043)");
        fixture.VerifySession();
        fixture.Owner.Status.Should().Be(UserStatus.Locked);
        fixture.Owner.FailedLoginAttempts.Should().Be(failures);
        fixture.Owner.LockoutEnd.Should().Be(end);
        fixture.VerifyLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning
                && entry.Values.Contains(PasskeySignalCodes.AdmittedWhileLocked))
            .Which.Values.Should().Contain(kind, "the line names the lock's kind ([S20-O7] counts Admin = 0)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(30.0)]
    public async Task AnAdministratorsLock_RefusesThePasskey_WithThePasswordSignInsAnswer(double? minutesLeft)
    {
        var end = minutesLeft is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : (DateTime?)null;
        using var fixture = new PasskeySignInFixture(status: UserStatus.Locked, failedLoginAttempts: 0, lockoutEnd: end);
        fixture.Owner.LockKind(DateTime.UtcNow).Should().Be(AccountLockKind.Admin);

        var result = await fixture.SignInAsync();

        result.FirstError.Code.Should().Be(fixture.Owner.LockRefusal(AccountLockKind.Admin).Code);
        result.FirstError.Code.Should().BeOneOf(UserErrors.AccountLocked.Code, UserErrors.AccountLockedUntil(end).Code);
        fixture.VerifyNoSession();
        fixture.VerifyLog.Entries.Should().NotContain(entry => entry.Values.Contains(PasskeySignalCodes.AdmittedWhileLocked));
    }

    [Fact]
    public async Task ALapsedLock_RefusesNothing_AndIsNoAdmissionUnderALock()
    {
        using var fixture = new PasskeySignInFixture(status: UserStatus.Locked, failedLoginAttempts: 5, lockoutEnd: DateTime.UtcNow.AddMinutes(-1));

        (await fixture.SignInAsync()).IsError.Should().BeFalse();
        fixture.VerifyLog.Entries.Should().NotContain(entry => entry.Values.Contains(PasskeySignalCodes.AdmittedWhileLocked));
    }

    [Theory]
    [InlineData(UserStatus.Inactive, "User.AccountInactive")]
    [InlineData(UserStatus.Pending, "User.AccountPending")]
    public async Task AnInactiveOrPendingAccount_AnswersWithItsOwnCode(UserStatus status, string code)
    {
        using var fixture = new PasskeySignInFixture(status: status);

        var result = await fixture.SignInAsync();

        result.FirstError.Code.Should().Be(code);
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task AnAccountWaitingForDeletion_LearnsWhenItsGracePeriodEnds_AndIsNotSignedIn()
    {
        using var fixture = new PasskeySignInFixture();
        fixture.OwnerDeleted = true;
        fixture.DeletedOwner = TestHelpers.CreateUser(id: fixture.Owner.Id, email: "owner@example.com", isDeleted: true, deletedAt: DateTime.UtcNow);
        var deletion = AccountDeletionRequest.Create(fixture.Owner.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", fixture.Owner.Id);
        fixture.Deletions.Setup(d => d.GetActiveByUserIdAsync(fixture.Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deletion);

        var result = await fixture.SignInAsync();

        result.FirstError.Code.Should().Be(UserErrors.AccountPendingDeletion(deletion.GraceEndsAtUtc).Code);
        result.FirstError.Should().BeEquivalentTo(UserErrors.AccountPendingDeletion(deletion.GraceEndsAtUtc),
            "the answer carries the end of the grace period, as the password sign-in's does");
        fixture.VerifyNoSession();
    }

    [Theory]
    [InlineData("deleted by its owner, grace period over", true)]
    [InlineData("deleted by an administrator, its status still Active", false)]
    public async Task AnyOtherDeletedAccount_IsAFailedCeremony_WithNoSession(string because, bool hadARequest)
    {
        using var fixture = new PasskeySignInFixture();
        fixture.OwnerDeleted = true;
        fixture.DeletedOwner = TestHelpers.CreateUser(
            id: fixture.Owner.Id, email: "owner@example.com", status: UserStatus.Active, isDeleted: true, deletedAt: DateTime.UtcNow);
        fixture.Deletions.Setup(d => d.GetActiveByUserIdAsync(fixture.Owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hadARequest ? Completed(fixture.Owner.Id) : null);

        var result = await fixture.SignInAsync();

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, because);
        fixture.VerifyNoSession();
    }

    private static AccountDeletionRequest Completed(Guid userId)
    {
        var request = AccountDeletionRequest.Create(userId, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", userId);
        request.Cancel();
        return request;
    }
}
