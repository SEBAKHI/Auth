using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Queries;

/// <summary>
/// Query to get a user's active sessions.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="CurrentSessionId">The ID of the current session (to mark as current).</param>
public record GetUserSessionsQuery(
    Guid UserId,
    Guid? CurrentSessionId = null) : IRequest<ErrorOr<IReadOnlyList<SessionDto>>>;
