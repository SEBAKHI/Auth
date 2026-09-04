using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// The rule that tells an automatic lock (raised by wrong passwords) from an
/// administrator's lock, without a schema change: the failure counter. It
/// decides whether a familiar device may still sign in and whether a completed
/// reset or email verification may clear the lock, so both halves are pinned.
/// </summary>
public class UserLockClassificationTests
{
    private const int MaxFailedAttempts = 5;

    [Fact]
    public void Lock_StartsANewEpisode_ByZeroingTheFailureCounter()
    {
        // An administrator locking an account that is being brute-forced must not
        // inherit the attacker's count: that is what would make a timed
        // administrative lock read as automatic.
        var user = Create(UserStatus.Active, failedLoginAttempts: 5, lockoutEnd: null);

        user.Lock(DateTime.UtcNow.AddHours(24), Guid.NewGuid());

        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut().Should().BeTrue();
        user.IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByFailedAttempts_IsTrue_OnlyForATimedLockWithAFullCount()
    {
        Create(UserStatus.Locked, 5, DateTime.UtcNow.AddMinutes(15))
            .IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeTrue("the counter raised it");
        Create(UserStatus.Locked, 0, null)
            .IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeFalse("indefinite means administrative");
        Create(UserStatus.Locked, 0, DateTime.UtcNow.AddHours(24))
            .IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeFalse("timed with an empty counter means administrative");
        Create(UserStatus.Locked, 5, DateTime.UtcNow.AddMinutes(-1))
            .IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeFalse("an expired lock is not a lock");
        Create(UserStatus.Active, 5, null)
            .IsLockedByFailedAttempts(MaxFailedAttempts).Should().BeFalse("not locked at all");
    }

    private static User Create(UserStatus status, int failedLoginAttempts, DateTime? lockoutEnd) => new(
        id: Guid.NewGuid(),
        email: "test@example.com",
        normalizedEmail: "TEST@EXAMPLE.COM",
        passwordHash: "hashed_password",
        firstName: "John",
        lastName: "Doe",
        displayName: "John Doe",
        phoneNumber: null,
        status: status,
        emailConfirmed: true,
        phoneConfirmed: false,
        twoFactorEnabled: false,
        twoFactorSecret: null,
        failedLoginAttempts: failedLoginAttempts,
        lockoutEnd: lockoutEnd,
        lastLoginAt: null,
        passwordChangedAt: DateTime.UtcNow,
        mustChangePassword: false,
        preferredLanguage: "en",
        timeZone: "UTC",
        metadata: null,
        isSystemUser: false,
        createdAt: DateTime.UtcNow,
        createdBy: Guid.NewGuid(),
        modifiedAt: null,
        modifiedBy: null);
}
