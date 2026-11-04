namespace Auth.Domain.Constants;

/// <summary>
/// The event codes of an application's two-step requirement (S23), written as
/// <c>{EventCode}</c> in the message template so an alert rule or the owner's weekly
/// count matches the code and never the wording.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the operator's contract: the readiness reading before
/// <c>Enforce</c> counts <see cref="WouldRequire"/>, and the condition for removing the
/// <c>IdentityProvider:ApplicationMfaEnforcement</c> switch counts <see cref="Refused"/>.
/// A value is never renamed or reused. <c>AppMfaSignalCodesTests</c> pins each one and
/// the format every signal code follows.
/// </para>
/// <para>
/// A line carries the application code, the account id, the outcome and the path
/// (authorize, exchange, refresh). It never carries a token, a ticket, an address or a
/// user agent.
/// </para>
/// </remarks>
public static class AppMfaSignalCodes
{
    /// <summary>
    /// Under <c>Monitor</c>: a session that did not prove two steps was let through to an
    /// application that requires them. Information: what <c>Enforce</c> would have stopped.
    /// </summary>
    public const string WouldRequire = "app-mfa.would-require";

    /// <summary>
    /// Under <c>Enforce</c>: a code exchange or a refresh of an application that requires
    /// two steps was refused because its session did not prove them, and the refresh's
    /// session was ended. Warning.
    /// </summary>
    public const string Refused = "app-mfa.refused";

    /// <summary>
    /// Under <c>Enforce</c>: a person came back to the authorize endpoint without the
    /// progress the last redirect asked for, and the request ended with
    /// <c>access_denied</c> instead of another redirect. Warning.
    /// </summary>
    public const string GuardDenied = "app-mfa.guard-denied";

    /// <summary>Every code above, for the format and uniqueness checks.</summary>
    public static IReadOnlyList<string> All { get; } = [WouldRequire, Refused, GuardDenied];
}
