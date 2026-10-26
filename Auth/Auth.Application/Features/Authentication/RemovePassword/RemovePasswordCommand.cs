using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RemovePassword;

/// <summary>
/// Removes the signed-in account's password (OI-215, D-215-3): from then on it signs
/// in with a code emailed to its address. The screen asks for a high-impact
/// confirmation before it sends this.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="CurrentSessionId">The session this request was made from: the recent sign-in it is judged by, and the one kept.</param>
/// <param name="IdpSessionToken">The browser's own SSO cookie, kept signed in like the session.</param>
public record RemovePasswordCommand(
    Guid UserId,
    Guid? CurrentSessionId,
    string? IdpSessionToken) : IRequest<ErrorOr<Success>>;
