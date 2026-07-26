using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Composes session termination, refresh-token revocation, session-id
/// blacklisting and IdP SSO session removal into the single credential-kill
/// primitive shared by the session-management endpoints and every account
/// deletion flow.
/// </summary>
public class CredentialRevocationService : ICredentialRevocationService
{
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly ITokenBlacklistService _blacklistService;
    private readonly ILogger<CredentialRevocationService> _logger;

    public CredentialRevocationService(
        IUserSessionRepository sessionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IIdpSessionRepository idpSessionRepository,
        ITokenBlacklistService blacklistService,
        ILogger<CredentialRevocationService> logger)
    {
        _sessionRepository = sessionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _idpSessionRepository = idpSessionRepository;
        _blacklistService = blacklistService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> TerminateSessionsAsync(
        Guid userId,
        Guid? exceptSessionId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        // Get the active sessions before termination so each one can be
        // individually killed for real afterwards.
        var activeSessions = await _sessionRepository.GetActiveSessionsForUserAsync(
            userId,
            sortBy: null,
            SortDirection.Asc,
            cancellationToken);

        var sessionsToKill = (exceptSessionId.HasValue
            ? activeSessions.Where(s => s.Id != exceptSessionId.Value)
            : activeSessions).ToList();

        if (exceptSessionId.HasValue)
        {
            await _sessionRepository.TerminateOtherSessionsAsync(
                userId, exceptSessionId.Value, reason, cancellationToken);
        }
        else
        {
            await _sessionRepository.TerminateAllForUserAsync(userId, reason, cancellationToken);
        }

        // Truly kill each terminated session: revoke its refresh tokens and
        // blacklist the session id so its existing access tokens are rejected.
        foreach (var session in sessionsToKill)
        {
            await _refreshTokenRepository.RevokeBySessionIdAsync(
                session.Id, revokedBy, reason, cancellationToken);
            _blacklistService.BlacklistSession(session.Id.ToString(), session.ExpiresAt);
        }

        _logger.LogInformation(
            "Terminated {SessionCount} sessions for user {UserId}",
            sessionsToKill.Count, userId);

        return sessionsToKill.Count;
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllCredentialsAsync(
        Guid userId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        var terminated = await TerminateSessionsAsync(
            userId, exceptSessionId: null, revokedBy, reason, cancellationToken);

        // Session-less refresh tokens (legacy/device flows) and the IdP SSO
        // sessions used by the OIDC authorize flow.
        await _refreshTokenRepository.RevokeAllForUserAsync(userId, revokedBy, reason, cancellationToken);
        await _idpSessionRepository.RevokeAllForUserAsync(userId, cancellationToken);

        _logger.LogInformation(
            "Revoked all credentials for user {UserId}: {Reason}",
            userId, reason);

        return terminated;
    }
}
