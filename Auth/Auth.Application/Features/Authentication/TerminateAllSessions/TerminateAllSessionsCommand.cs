using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.TerminateAllSessions;

/// <summary>
/// Command to terminate all sessions for a user except the current one.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="ExceptSessionId">Optional session ID to exclude (usually the current session).</param>
/// <param name="IdpSessionToken">
/// The caller's SSO cookie value, if the browser presented one, so signing out
/// everywhere ends the OTHER browsers' SSO sessions without ending this one.
/// </param>
public record TerminateAllSessionsCommand(
    Guid UserId,
    Guid? ExceptSessionId = null,
    string? IdpSessionToken = null) : IRequest<ErrorOr<int>>;
