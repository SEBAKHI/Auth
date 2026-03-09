using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.TerminateAllSessions;

/// <summary>
/// Handler for the terminate all sessions command.
/// </summary>
public class TerminateAllSessionsCommandHandler : IRequestHandler<TerminateAllSessionsCommand, ErrorOr<int>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ILogger<TerminateAllSessionsCommandHandler> _logger;

    public TerminateAllSessionsCommandHandler(
        IUserSessionRepository sessionRepository,
        ILogger<TerminateAllSessionsCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<int>> Handle(
        TerminateAllSessionsCommand request,
        CancellationToken cancellationToken)
    {
        // Get count of active sessions before termination for logging
        var activeSessions = await _sessionRepository.GetActiveSessionsForUserAsync(
            request.UserId,
            cancellationToken);

        var sessionsToTerminate = request.ExceptSessionId.HasValue
            ? activeSessions.Count(s => s.Id != request.ExceptSessionId.Value)
            : activeSessions.Count;

        if (request.ExceptSessionId.HasValue)
        {
            await _sessionRepository.TerminateOtherSessionsAsync(
                request.UserId,
                request.ExceptSessionId.Value,
                "User terminated all other sessions",
                cancellationToken);
        }
        else
        {
            await _sessionRepository.TerminateAllForUserAsync(
                request.UserId,
                "User terminated all sessions",
                cancellationToken);
        }

        _logger.LogInformation(
            "Terminated {SessionCount} sessions for user {UserId}",
            sessionsToTerminate, request.UserId);

        return sessionsToTerminate;
    }
}
