namespace Auth.Domain.Constants;

/// <summary>
/// The event codes of the emailed second factor's log lines, written as
/// <c>{EventCode}</c> in the message template so an alert rule matches the code and
/// never the wording.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the operator's contract: the alert rules of the emailed
/// code (A128, A129) and the 90-day review of its delivery exception search for
/// them. A value is never renamed or reused. <c>TwoFactorSignalCodesTests</c> pins
/// each one and the format every signal code follows.
/// </para>
/// <para>
/// A line carries the account id and the reason. It never carries the code, the
/// email address, the client address, the device id or the user agent.
/// </para>
/// </remarks>
public static class TwoFactorSignalCodes
{
    /// <summary>
    /// A code could not be emailed for a sign-in or a recovery whose second factor is
    /// email: email sending is off, or the account's factor is not the emailed code.
    /// Warning: the person is left with their recovery codes.
    /// </summary>
    public const string EmailCodeUnavailable = "two-factor.email-code-unavailable";

    /// <summary>
    /// A request for an emailed code was refused by a limit: the challenge's three
    /// sends, the account's per-window or daily cap, or a locked factor. Warning.
    /// </summary>
    public const string EmailCodeThrottled = "two-factor.email-code-throttled";

    /// <summary>Every code above, for the format and uniqueness checks.</summary>
    public static IReadOnlyList<string> All { get; } = [EmailCodeUnavailable, EmailCodeThrottled];
}
