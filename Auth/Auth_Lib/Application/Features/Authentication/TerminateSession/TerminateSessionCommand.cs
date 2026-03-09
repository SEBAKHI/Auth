using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.TerminateSession;

/// <summary>
/// Command to terminate a specific user session.
/// </summary>
/// <param name="UserId">The ID of the user who owns the session.</param>
/// <param name="SessionId">The ID of the session to terminate.</param>
public record TerminateSessionCommand(
    Guid UserId,
    Guid SessionId) : IRequest<ErrorOr<Success>>;
