using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The lock columns an administrator's status action writes, decided by the
/// account (<see cref="Entities.User.Lock"/>, <see cref="Entities.User.Unlock"/>,
/// <see cref="Entities.User.Activate"/>, <see cref="Entities.User.Deactivate"/>)
/// and written by one statement, never by a whole-row update built from a read
/// that may be minutes old.
/// </summary>
/// <remarks>
/// Lock writes Locked, a count of 0 and its expiry; Unlock and Activate write
/// Active, 0 and no expiry; Deactivate writes Inactive and leaves the count and the
/// expiry as they are.
/// </remarks>
public sealed record AccountStatusChange
{
    private AccountStatusChange(UserStatus status, bool resetsLock, DateTime? lockoutEndUtc)
    {
        Status = status;
        ResetsLock = resetsLock;
        LockoutEndUtc = lockoutEndUtc;
    }

    /// <summary>The status written.</summary>
    public UserStatus Status { get; }

    /// <summary>
    /// Whether the count is zeroed and the expiry replaced by
    /// <see cref="LockoutEndUtc"/>. False leaves both untouched.
    /// </summary>
    public bool ResetsLock { get; }

    /// <summary>The expiry written when <see cref="ResetsLock"/> is true.</summary>
    public DateTime? LockoutEndUtc { get; }

    /// <summary>An administrator's lock, with or without an expiry. It starts a new episode at 0.</summary>
    public static AccountStatusChange Lock(DateTime? lockoutEndUtc) => new(UserStatus.Locked, true, lockoutEndUtc);

    /// <summary>An administrator's unlock: active, at 0, with no expiry.</summary>
    public static AccountStatusChange Unlock() => new(UserStatus.Active, true, null);

    /// <summary>An administrator's activation: active, at 0, with no expiry.</summary>
    public static AccountStatusChange Activate() => new(UserStatus.Active, true, null);

    /// <summary>An administrator's deactivation: inactive; the lock columns are left alone.</summary>
    public static AccountStatusChange Deactivate() => new(UserStatus.Inactive, false, null);
}
