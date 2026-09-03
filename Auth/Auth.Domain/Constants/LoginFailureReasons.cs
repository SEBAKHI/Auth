namespace Auth.Domain.Constants;

/// <summary>
/// The prose written to <c>LoginAttempts.FailureReason</c> by the sign-in
/// handlers. Named here because two readers depend on the exact text: the
/// per-address lockout ceiling counts only <see cref="InvalidPassword"/> rows,
/// and the dashboard's locked-out metric matches <see cref="AccountLocked"/>.
/// A retyped literal in either place would silently zero a control.
/// </summary>
public static class LoginFailureReasons
{
    /// <summary>A password was presented and did not verify.</summary>
    public const string InvalidPassword = "Invalid password";

    /// <summary>Refused because the account was locked at the time.</summary>
    public const string AccountLocked = "Account locked";

    /// <summary>
    /// Refused because this client address had already spent its own allowance
    /// of wrong passwords against this account. Recorded for the audit trail;
    /// deliberately not counted toward the ceiling it reports.
    /// </summary>
    public const string SourceLocked = "Source locked";
}
