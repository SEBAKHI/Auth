namespace Auth.Application.DTOs;

/// <summary>
/// A response that hands a browser a session: a sign-in (<see cref="LoginResponse"/>)
/// or a refresh (<see cref="TokenResponse"/>). The HTTP layer reads it to decide how
/// the session's credentials are delivered — the SSO token always into its cookie,
/// the refresh token into the body or into a cookie — without the handlers knowing
/// anything about HTTP.
/// </summary>
/// <remarks>
/// Implemented explicitly on both records, so none of these members is serialized.
/// </remarks>
public interface ISessionIssuingResponse
{
    /// <summary>
    /// Gets the tokens this response issues; <c>null</c> when it issues none yet
    /// (a sign-in waiting on its second factor).
    /// </summary>
    TokenResponse? IssuedTokens { get; }

    /// <summary>
    /// Gets the plain IdP session token minted with this sign-in, if any.
    /// </summary>
    string? IssuedIdpSessionToken { get; }

    /// <summary>
    /// Gets the id of the IdP session minted with this sign-in, if any (OI-154):
    /// when it replaces the same person's previous session in this browser, that
    /// session's refresh chains move onto it.
    /// </summary>
    Guid? IssuedIdpSessionId { get; }

    /// <summary>
    /// Gets whether this response's sign-in cookies outlive the browser ("Stay
    /// signed in", OI-154): for a sign-in, whether the IdP session it minted is
    /// persistent (false when it minted none); null for a refresh, whose cookie
    /// follows the IdP session the request presents.
    /// </summary>
    bool? IssuedSessionIsPersistent { get; }

    /// <summary>
    /// Gets the user the tokens were issued to, when the response knows it. A
    /// refresh's cookie may follow the presented IdP session only when that
    /// session is this user's (OI-154): another person's «Stay signed in» on the
    /// same browser must not keep this person's sign-in.
    /// </summary>
    Guid? IssuedToUserId { get; }

    /// <summary>
    /// Returns a copy whose refresh token reads <paramref name="refreshToken"/>.
    /// </summary>
    ISessionIssuingResponse WithRefreshToken(string refreshToken);
}
