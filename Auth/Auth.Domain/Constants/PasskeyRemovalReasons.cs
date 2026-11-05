namespace Auth.Domain.Constants;

/// <summary>
/// Why a passkey stopped working (S20, R-ENUM): the value of
/// <c>UserPasskeys.RevokedReason</c> and of <c>PasskeyRemovedEvent.Reason</c>. Written by
/// code and by the scripts the owner runs, read by the audit trail and the notices, so the
/// values are fixed strings, declared once and never localized or reused.
/// </summary>
public static class PasskeyRemovalReasons
{
    /// <summary>Its owner removed it.</summary>
    public const string User = "user";

    /// <summary>An administrator reset the account's second factor (M8(4)).</summary>
    public const string PeerReset = "peer-reset";

    /// <summary>It was registered in the 72 hours before a completed password reset (D-T19 D).</summary>
    public const string PasswordReset = "password-reset";

    /// <summary>It was registered in the 72 hours before a password change (D-T19 D).</summary>
    public const string PasswordChange = "password-change";

    /// <summary>It was registered in the 72 hours before an administrator locked the account (D-T19 D).</summary>
    public const string AdminLock = "admin-lock";

    /// <summary>The RP ID moved ([S20-O10]): written only by the SQL Auth_Setup prints.</summary>
    public const string RpIdMove = "rp-id-move";

    /// <summary>The owner's emergency reset (M8(5)): written only by the SQL Auth_Setup prints.</summary>
    public const string BreakGlass = "break-glass";

    /// <summary>
    /// How long before a password reset, a password change or an administrator's lock a
    /// passkey must have been registered to survive it (D-T19 D). One registered later may
    /// be the planted door of whoever held the session; an older one is the owner's and is
    /// listed in the notice instead.
    /// </summary>
    public static readonly TimeSpan RecentRegistrationWindow = TimeSpan.FromHours(72);
}
