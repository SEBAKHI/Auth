using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.TerminateSession;

/// <summary>
/// Handler for the terminate session command.
/// </summary>
public class TerminateSessionCommandHandler : IRequestHandler<TerminateSessionCommand, ErrorOr<Success>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ILogger<TerminateSessionCommandHandler> _logger;

    public TerminateSessionCommandHandler(
        IUserSessionRepository sessionRepository,
        ILogger<TerminateSessionCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        TerminateSessionCommand request,
        CancellationToken cancellationToken)
    {
        // Verify the session exists and belongs to the user
        var session = await _sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        if (session == null)
        {
            return SessionErrors.SessionNotFound;
        }

        if (session.UserId != request.UserId)
        {
            _logger.LogWarning(
                "User {UserId} attempted to terminate session {SessionId} belonging to another user",
                request.UserId, request.SessionId);
            return SessionErrors.SessionNotFound; // Don't reveal that session exists
        }

        if (!session.IsActive)
        {
            return SessionErrors.SessionAlreadyTerminated;
        }

        await _sessionRepository.TerminateAsync(request.SessionId, "User terminated", cancellationToken);

        _logger.LogInformation(
            "Session {SessionId} terminated for user {UserId}",
            request.SessionId, request.UserId);

        return Result.Success;
    }
}
