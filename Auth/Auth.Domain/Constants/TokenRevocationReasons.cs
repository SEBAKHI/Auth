namespace Auth.Domain.Constants;

/// <summary>
/// Reasons persisted to <c>RefreshTokens.ReasonRevoked</c> and, for the paths
/// that also end sessions, <c>UserSessions.EndReason</c>.
///
/// These strings are the ONLY thing distinguishing an ordinary rotation from a
/// suspected theft after the fact, so they are read by operators triaging
/// incidents and must not drift between the places that write them.
/// </summary>
public static class TokenRevocationReasons
{
    /// <summary>
    /// The token was spent normally and replaced by its successor. The overwhelmingly
    /// common reason, and the state a stolen token is also in once the legitimate
    /// holder has refreshed - which is why it cannot be used to tell the two apart.
    /// </summary>
    public const string Rotated = "Rotated";

    /// <summary>
    /// A token that had already been spent was presented a second time. Every
    /// token and session the account holds is revoked in response.
    /// </summary>
    public const string RefreshTokenReuse = "Detected refresh token reuse";

    /// <summary>
    /// An authorization code that had already been redeemed was presented again.
    /// Only the session that code produced is revoked, not the account: the code
    /// bought entry to one application on one device, so that is the whole of
    /// what a thief holding it could have taken.
    /// </summary>
    public const string AuthorizationCodeReplay = "Detected authorization code replay";

    /// <summary>
    /// The holder lost their entitlement to the application this token is scoped
    /// to — their invitation was withdrawn, or the application stopped being open
    /// to everyone. Only that application's token is revoked; tokens for other
    /// applications and for the platform are untouched.
    /// </summary>
    public const string ApplicationAccessRevoked = "Application access revoked";

    /// <summary>
    /// The application itself was switched off, so every token scoped to it is
    /// revoked at once.
    /// </summary>
    public const string ApplicationDeactivated = "Application deactivated";

    /// <summary>
    /// The application was deleted, so every token scoped to it is revoked at once,
    /// before the row is soft-deleted.
    /// </summary>
    public const string ApplicationDeleted = "Application deleted";

    /// <summary>
    /// The holder retired the token at the revocation endpoint (RFC 7009). A refresh
    /// token's whole session goes with it: the row, every refresh token issued under
    /// it, and the access tokens already out.
    /// </summary>
    public const string RevocationRequested = "Token revocation requested";

    /// <summary>
    /// A platform app's own sign-in (no application) outlived the IdP session it
    /// was born with: signed out, replaced by another person's sign-in in that
    /// browser, past its lifetime, or idle without "Stay signed in" (OI-154). The
    /// token's whole session goes with it.
    /// </summary>
    public const string IdpSessionEnded = "IdP session ended";

    /// <summary>
    /// A server-side application signed the person out with its key and the session's bound
    /// refresh token (OI-183): the application's session goes, and the IdP session the token was
    /// linked to ends with it, so the browser's next sign-in asks for the password.
    /// </summary>
    public const string SignedOutByApplication = "Signed out by the application";

    /// <summary>
    /// The IdP session this sign-in was born from was ended on purpose in its browser: a sign-out
    /// there, or another person signing in on it (single logout, OI-155). The token's whole session
    /// goes with it, whichever application holds it. Never written for a session that went idle or
    /// reached its maximum.
    /// </summary>
    public const string SingleLogout = "Single logout";

    /// <summary>
    /// The application requires two-step verification and this session never proved it
    /// (S23, AM6): its next refresh under <c>Enforce</c> ends the session, so a sign-in
    /// made before the requirement was switched on does not outlive it. Only that session
    /// goes; the person's other applications are untouched.
    /// </summary>
    public const string ApplicationMfaRequired = "Application requires two-step verification";

    /// <summary>
    /// The passkey a sign-in proved was revoked while that sign-in wrote its session (S20 PR B,
    /// REVIEW 151 F2): a removal, an administrator's reset, a lock, or a password change or reset
    /// got there in between. The session just issued goes, and its tokens are never returned.
    /// </summary>
    public const string PasskeyRevokedMeanwhile = "Passkey revoked during the sign-in";
}
