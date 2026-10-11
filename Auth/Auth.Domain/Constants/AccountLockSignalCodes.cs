namespace Auth.Domain.Constants;

/// <summary>
/// The event codes of the account-lock log lines: one line per transition of the
/// lock columns, written as <c>{EventCode}</c> in the message template so an alert
/// rule matches the code and never the wording.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the operator's contract: dashboards, alert rules and
/// the day-16 review of X12 search for them. A value is never renamed or reused.
/// <c>AccountLockSignalCodesTests</c> pins each one and the format every signal
/// code follows.
/// </para>
/// <para>
/// A line carries the account id, the count and the expiry. It never carries the
/// email address, the password, the client address, the device id or the user
/// agent.
/// </para>
/// </remarks>
public static class AccountLockSignalCodes
{
    /// <summary>Wrong passwords started (or extended) a timed lock. Warning.</summary>
    public const string Locked = "account.locked";

    /// <summary>Wrong passwords reached the consecutive-failure limit: the lock has no expiry. Warning.</summary>
    public const string HardLocked = "account.hard-locked";

    /// <summary>
    /// The second-factor counter reached the consecutive-failure limit. Warning.
    /// Declared here with its siblings; it is written once the second-factor cap ships.
    /// </summary>
    public const string TwoFactorHardLocked = "two-factor.hard-locked";

    /// <summary>
    /// A live automatic lock was lifted: a sign-in proved the password, the password
    /// was reset or changed. Warning when the lock was hard, Information otherwise.
    /// </summary>
    public const string Unlocked = "account.unlocked";

    /// <summary>
    /// A familiar source was let through to the password check while an automatic
    /// lock was live. Warning when the lock was hard, Information otherwise.
    /// </summary>
    public const string FamiliarAdmitted = "account.familiar-admitted";

    /// <summary>Every code above, for the format and uniqueness checks.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Locked, HardLocked, TwoFactorHardLocked, Unlocked, FamiliarAdmitted];
}
