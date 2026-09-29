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
    /// Returns a copy whose refresh token reads <paramref name="refreshToken"/>.
    /// </summary>
    ISessionIssuingResponse WithRefreshToken(string refreshToken);
}
