using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.TerminateSession;

/// <summary>
/// Handler for the terminate session command.
/// </summary>
public class TerminateSessionCommandHandler : IRequestHandler<TerminateSessionCommand, ErrorOr<Success>>
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenBlacklistService _blacklistService;
    private readonly ILogger<TerminateSessionCommandHandler> _logger;

    public TerminateSessionCommandHandler(
        IUserSessionRepository sessionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenBlacklistService blacklistService,
        ILogger<TerminateSessionCommandHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _blacklistService = blacklistService;
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

        // Truly kill the session: revoke its refresh tokens (durable) and
        // blacklist the session id so existing access tokens are rejected at once.
        await _refreshTokenRepository.RevokeBySessionIdAsync(
            request.SessionId, request.UserId, "Session terminated", cancellationToken);
        _blacklistService.BlacklistSession(request.SessionId.ToString(), session.ExpiresAt);

        _logger.LogInformation(
            "Session {SessionId} terminated for user {UserId}",
            request.SessionId, request.UserId);

        return Result.Success;
    }
}
