using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.LogoutWithRefreshCookie;

/// <summary>
/// Signs a browser out with nothing but the refresh token its first-party
/// refresh cookie holds — for when the bearer-authenticated sign-out cannot be
/// used (the access token already expired or was refused), or when an earlier
/// sign-out never reached the server.
/// </summary>
/// <param name="RefreshToken">The refresh token read from the app's HttpOnly cookie.</param>
/// <param name="ExpectedSessionId">
/// The session the browser meant to end, when it knows it. If the cookie now
/// belongs to another session (a new sign-in happened since), nothing is ended.
/// </param>
/// <param name="IdpSessionToken">The browser's SSO cookie value, ended with the session.</param>
public record LogoutWithRefreshCookieCommand(
    string RefreshToken,
    Guid? ExpectedSessionId,
    string? IdpSessionToken) : IRequest<ErrorOr<LogoutWithRefreshCookieResult>>;

/// <summary>What a cookie sign-out did.</summary>
/// <param name="Ended">
/// True when the cookie's session is over — ended now, or already dead — so the
/// cookie can go. False when the cookie belongs to a different session than the
/// one the browser meant to end, and must stay.
/// </param>
public record LogoutWithRefreshCookieResult(bool Ended);
