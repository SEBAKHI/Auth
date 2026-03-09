using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.TerminateAllSessions;

/// <summary>
/// Command to terminate all sessions for a user except the current one.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="ExceptSessionId">Optional session ID to exclude (usually the current session).</param>
public record TerminateAllSessionsCommand(
    Guid UserId,
    Guid? ExceptSessionId = null) : IRequest<ErrorOr<int>>;
