using Auth.Domain.Enums;

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
}
