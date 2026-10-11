namespace Auth.Domain.Enums;

/// <summary>
/// Which lock, if any, an account's row holds at a given instant. Read from the
/// three lock columns alone (<c>Status</c>, the consecutive-failure count and
/// <c>LockoutEndUtc</c>), so no schema change was needed to tell them apart; see
/// <see cref="ValueObjects.AccountLockState.Kind"/> for the table.
/// </summary>
/// <remarks>
/// The values are fixed and never renumbered: log lines and tests name them.
/// </remarks>
public enum AccountLockKind
{
    /// <summary>Not locked: any status other than <see cref="UserStatus.Locked"/>.</summary>
    None = 0,

    /// <summary>
    /// Locked by wrong passwords for a while: a failure count above zero and an
    /// expiry still ahead. It ends by itself.
    /// </summary>
    Timed = 1,

    /// <summary>
    /// Locked by wrong passwords with no expiry: the count reached the
    /// consecutive-failure limit. Only a password proof, a reset or an
    /// administrator lifts it.
    /// </summary>
    Hard = 2,

    /// <summary>
    /// Locked by an administrator: a failure count of zero, with no expiry or one
    /// still ahead. No self-service path lifts it.
    /// </summary>
    Admin = 3,

    /// <summary>
    /// Still marked locked, but its expiry has passed. Nothing is refused; the next
    /// sign-in repairs the row.
    /// </summary>
    Lapsed = 4
}
