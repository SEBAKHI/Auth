using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.TerminateAllSessions;

/// <summary>
/// Handler for the terminate all sessions command.
/// </summary>
public class TerminateAllSessionsCommandHandler : IRequestHandler<TerminateAllSessionsCommand, ErrorOr<int>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenBlacklistService _blacklistService;
    private readonly ILogger<TerminateAllSessionsCommandHandler> _logger;

    public TerminateAllSessionsCommandHandler(
        IUserSessionRepository sessionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenBlacklistService blacklistService,
        ILogger<TerminateAllSessionsCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _blacklistService = blacklistService;
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

        var sessionsToKill = (request.ExceptSessionId.HasValue
            ? activeSessions.Where(s => s.Id != request.ExceptSessionId.Value)
            : activeSessions).ToList();

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

        // Truly kill each terminated session: revoke its refresh tokens and
        // blacklist the session id so its existing access tokens are rejected.
        foreach (var session in sessionsToKill)
        {
            await _refreshTokenRepository.RevokeBySessionIdAsync(
                session.Id, request.UserId, "Session terminated", cancellationToken);
            _blacklistService.BlacklistSession(session.Id.ToString(), session.ExpiresAt);
        }

        _logger.LogInformation(
            "Terminated {SessionCount} sessions for user {UserId}",
            sessionsToKill.Count, request.UserId);

        return sessionsToKill.Count;
    }
}
