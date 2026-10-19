namespace Auth.Domain.Constants;

/// <summary>
/// What ended a browser's IdP session on purpose, so that every sign-in born from it ended too
/// (single logout, OI-155). Recorded on the audit row; short codes, not sentences.
/// </summary>
public static class SingleLogoutTriggers
{
    /// <summary>
    /// The person signed out in that browser: the console, the accounts app, or the end-session
    /// confirmation page.
    /// </summary>
    public const string SignOut = "SignOut";

    /// <summary>
    /// A server-side application signed the person out from its server, with its key and the
    /// session's bound refresh token (OI-183).
    /// </summary>
    public const string ServerSignOut = "ServerSignOut";

    /// <summary>Another person signed in on that browser, replacing the session.</summary>
    public const string OtherUserSignedIn = "OtherUserSignedIn";
}
