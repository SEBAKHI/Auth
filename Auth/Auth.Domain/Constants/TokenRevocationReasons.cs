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
}
