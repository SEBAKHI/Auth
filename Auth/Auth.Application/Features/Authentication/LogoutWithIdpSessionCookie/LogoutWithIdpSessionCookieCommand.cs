using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.LogoutWithIdpSessionCookie;

/// <summary>
/// Signs a browser out when its first-party app no longer holds a refresh cookie, but the
/// browser still holds its SSO cookie (OI-155). Once the SSO session went idle, the app's own
/// refresh was refused and that refusal deleted the refresh cookie, so the app's sign-out arrives
/// with the SSO cookie and the session id the app last knew, and nothing else.
/// </summary>
/// <param name="SessionId">The app's own session, from its last access token.</param>
/// <param name="IdpSessionToken">The browser's SSO cookie value.</param>
public record LogoutWithIdpSessionCookieCommand(
    Guid SessionId,
    string IdpSessionToken) : IRequest<ErrorOr<LogoutWithIdpSessionCookieResult>>;

/// <summary>What the sign-out did.</summary>
/// <param name="IdpSessionEnded">
/// True when the SSO session was the signing-out person's and was ended, so its cookie can go.
/// False when it was not theirs, named nothing, or single logout is off: the cookie stays.
/// </param>
public record LogoutWithIdpSessionCookieResult(bool IdpSessionEnded);
