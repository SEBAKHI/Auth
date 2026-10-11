using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The three lock columns of one <c>Users</c> row, as read or as a statement left
/// them, and the one classifier of what they mean.
/// </summary>
/// <remarks>
/// <para>
/// Every decision about a lock — refuse, admit from a familiar source, repair,
/// clear — asks <see cref="Kind"/>, and nothing else infers a lock's origin.
/// The SQL statements in the user repository encode the same table, and
/// <c>LockSqlTextGuardTests</c> holds the two together:
/// </para>
/// <list type="table">
/// <item><term>Admin</term><description>Locked, count 0, no expiry or one ahead.</description></item>
/// <item><term>Timed</term><description>Locked, count above 0, expiry ahead.</description></item>
/// <item><term>Hard</term><description>Locked, count above 0, no expiry.</description></item>
/// <item><term>Lapsed</term><description>Locked, expiry passed (any count).</description></item>
/// <item><term>None</term><description>Any other status.</description></item>
/// </list>
/// <para>
/// An administrator's lock is told from an automatic one by the count alone,
/// never by a missing expiry: an administrator may lock with or without one, and
/// the hard lock has none. <see cref="Entities.User.Lock"/> zeroes the count, and
/// the failure statement never counts against a live administrator's lock.
/// </para>
/// </remarks>
public readonly record struct AccountLockState
{
    public AccountLockState(UserStatus status, int failedAttempts, DateTime? lockoutEndUtc)
    {
        Status = status;
        FailedAttempts = failedAttempts;
        LockoutEndUtc = lockoutEndUtc;
    }

    /// <summary>The account's status.</summary>
    public UserStatus Status { get; }

    /// <summary>Consecutive wrong passwords since the last password proof, reset or administrator action.</summary>
    public int FailedAttempts { get; }

    /// <summary>When a timed lock ends; null for a hard lock and for most administrator locks.</summary>
    public DateTime? LockoutEndUtc { get; }

    /// <summary>
    /// Which lock these columns hold at <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="nowUtc">The instant to judge the expiry against, in UTC.</param>
    public AccountLockKind Kind(DateTime nowUtc)
    {
        if (Status != UserStatus.Locked)
        {
            return AccountLockKind.None;
        }

        if (LockoutEndUtc is { } end && end <= nowUtc)
        {
            return AccountLockKind.Lapsed;
        }

        if (FailedAttempts <= 0)
        {
            return AccountLockKind.Admin;
        }

        return LockoutEndUtc is null ? AccountLockKind.Hard : AccountLockKind.Timed;
    }

    /// <summary>
    /// Whether the lock refuses a sign-in at <paramref name="nowUtc"/>: timed,
    /// hard or administrative.
    /// </summary>
    public bool IsLive(DateTime nowUtc) =>
        Kind(nowUtc) is AccountLockKind.Timed or AccountLockKind.Hard or AccountLockKind.Admin;

    /// <summary>
    /// Whether the lock was raised by wrong passwords and is still live: timed or
    /// hard. Only these may be lifted by the account's owner.
    /// </summary>
    public bool IsAutomatic(DateTime nowUtc) =>
        Kind(nowUtc) is AccountLockKind.Timed or AccountLockKind.Hard;

    /// <summary>
    /// Whether the familiar-source door is sealed for this episode: the count has
    /// passed the hard limit by a full timed-lock threshold, so a familiar address
    /// that keeps guessing is refused like a stranger. A provider sign-in is never
    /// let through the door under the hard lock at all, so this concerns the
    /// password door only.
    /// </summary>
    /// <param name="hardLimit">The consecutive-failure limit (Hard′).</param>
    /// <param name="lockThreshold">The failures per timed lock (Max′).</param>
    public bool IsFamiliarDoorSealed(int hardLimit, int lockThreshold) =>
        FailedAttempts >= hardLimit + lockThreshold;

    /// <summary>
    /// The answer a sign-in gets when <paramref name="kind"/> refuses it. A hard
    /// lock names the way out (a password reset); a timed one says when it ends;
    /// an administrator's lock says what it always said.
    /// </summary>
    public Error LockRefusal(AccountLockKind kind) => kind == AccountLockKind.Hard
        ? UserErrors.AccountLockedResetRequired
        : UserErrors.AccountLockedUntil(LockoutEndUtc);
}
