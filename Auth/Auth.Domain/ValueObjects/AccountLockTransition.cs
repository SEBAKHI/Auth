using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// What one statement on the lock columns did to one account: the columns before
/// it and after it, read from the statement's own <c>OUTPUT</c> clause rather than
/// from a second read that a concurrent request could change in between.
/// </summary>
/// <remarks>
/// Each predicate here names one signal, so the decision "was this a lock, a hard
/// lock, an unlock" is made once, from the row the statement actually wrote.
/// </remarks>
public sealed record AccountLockTransition
{
    public AccountLockTransition(Guid userId, AccountLockState before, AccountLockState after)
    {
        UserId = userId;
        Before = before;
        After = after;
    }

    /// <summary>The account the statement changed.</summary>
    public Guid UserId { get; }

    /// <summary>The lock columns as the statement found them.</summary>
    public AccountLockState Before { get; }

    /// <summary>The lock columns as the statement left them.</summary>
    public AccountLockState After { get; }

    /// <summary>
    /// The statement started a timed lock, or extended one with a new expiry.
    /// </summary>
    public bool StartedTimedLock(DateTime nowUtc) =>
        After.Kind(nowUtc) == AccountLockKind.Timed
        && (Before.Kind(nowUtc) != AccountLockKind.Timed || Before.LockoutEndUtc != After.LockoutEndUtc);

    /// <summary>The statement moved the account into the hard lock.</summary>
    public bool StartedHardLock(DateTime nowUtc) =>
        After.Kind(nowUtc) == AccountLockKind.Hard && Before.Kind(nowUtc) != AccountLockKind.Hard;

    /// <summary>
    /// The statement lifted a live automatic lock (timed or hard). Repairing a lapsed
    /// one is not an unlock: the lock had already ended by itself.
    /// </summary>
    public bool LiftedAutomaticLock(DateTime nowUtc) =>
        Before.IsAutomatic(nowUtc) && After.Kind(nowUtc) == AccountLockKind.None;
}
