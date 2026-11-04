namespace Auth.Domain.Constants;

/// <summary>
/// The event codes of the token revocation list's load lines (X04, ARR-0501), written
/// as <c>{EventCode}</c> in the message template so a log filter or an alert rule keeps
/// them by the code, whatever level the deployment logs at.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the operator's contract: a deployment's check after a
/// restart searches for them. A value is never renamed or reused.
/// <c>RevocationSignalCodesTests</c> pins each one and the signal-code format.
/// </para>
/// <para>
/// A line carries a count, an attempt number and an elapsed time. It never carries a
/// token, a session id or an account id.
/// </para>
/// </remarks>
public static class RevocationSignalCodes
{
    /// <summary>
    /// The revocation list was loaded from durable storage, before the server took a
    /// request, or by the retry after a failed start. Written on every such load, a
    /// count of zero included. Information.
    /// </summary>
    public const string Rehydrated = "revocation.rehydrated";

    /// <summary>
    /// The revocation list could not be loaded (the database did not answer, or the
    /// load ran past its budget). Retried every 30 seconds until it succeeds. Critical.
    /// </summary>
    public const string RehydrationFailed = "revocation.rehydration-failed";

    /// <summary>Every code above, for the format and uniqueness checks.</summary>
    public static IReadOnlyList<string> All { get; } = [Rehydrated, RehydrationFailed];
}
