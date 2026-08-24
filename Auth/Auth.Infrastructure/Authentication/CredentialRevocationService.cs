using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly IOptionsMonitor<JwtSettings> _jwtSettings;
    private readonly ILogger<CredentialRevocationService> _logger;

    public CredentialRevocationService(
        IUserSessionRepository sessionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IIdpSessionRepository idpSessionRepository,
        ITokenBlacklistService blacklistService,
        IRefreshTokenKeyService tokenKeyService,
        IOptionsMonitor<JwtSettings> jwtSettings,
        ILogger<CredentialRevocationService> logger)
    {
        _sessionRepository = sessionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _idpSessionRepository = idpSessionRepository;
        _blacklistService = blacklistService;
        _tokenKeyService = tokenKeyService;
        _jwtSettings = jwtSettings;
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
    public async Task<int> TerminateSessionsByDeviceAsync(
        Guid userId,
        string deviceHash,
        string reason,
        CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetActiveByDeviceHashAsync(
            userId, deviceHash, cancellationToken);

        foreach (var session in sessions)
        {
            await _sessionRepository.TerminateAsync(session.Id, reason, cancellationToken);
            await _refreshTokenRepository.RevokeBySessionIdAsync(
                session.Id, userId, reason, cancellationToken);
            _blacklistService.BlacklistSession(session.Id.ToString(), session.ExpiresAt);
        }

        _logger.LogInformation(
            "Terminated {SessionCount} sessions for user {UserId} on one device",
            sessions.Count, userId);

        return sessions.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Auth.Domain.Entities.UserSession>> EnforceConcurrentSessionLimitAsync(
        Guid userId,
        int maxSessions,
        string reason,
        CancellationToken cancellationToken)
    {
        if (maxSessions <= 0)
        {
            return [];
        }

        // The repository decides which sessions lose and ends them in one
        // statement; only the rows it actually changed come back, so the kill
        // loop below cannot run twice for the same session when two sign-ins
        // race.
        var evicted = await _sessionRepository.TerminateBeyondLimitAsync(
            userId, maxSessions, reason, cancellationToken);

        foreach (var session in evicted)
        {
            // revokedBy is the user themselves: no administrator asked for this,
            // it is the account's own policy catching up with its own sign-in.
            await _refreshTokenRepository.RevokeBySessionIdAsync(
                session.Id, userId, reason, cancellationToken);
            _blacklistService.BlacklistSession(session.Id.ToString(), session.ExpiresAt);
        }

        if (evicted.Count > 0)
        {
            _logger.LogInformation(
                "Terminated {SessionCount} sessions for user {UserId} over the concurrent session limit of {MaxSessions}",
                evicted.Count, userId, maxSessions);
        }

        return evicted;
    }

    /// <inheritdoc />
    public async Task TerminateSessionAsync(
        Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken)
    {
        // Same three moves every other path here makes, aimed at one session:
        // end the row, revoke what can mint a new access token, and blacklist
        // the id so the access token already out there stops working now. Doing
        // only the first would end nothing — the refresh token would walk the
        // session straight back in.
        await _sessionRepository.TerminateAsync(sessionId, reason, cancellationToken);
        await _refreshTokenRepository.RevokeBySessionIdAsync(
            sessionId, revokedBy, reason, cancellationToken);

        // Held until the last moment a token issued right now could still be
        // accepted. That is NOT the access-token lifetime alone: validation adds
        // ClockSkew on top of exp, so a flat one-day horizon could lapse while a
        // token was still being honoured — the settings ceiling for the lifetime
        // is exactly one day, leaving no room for the skew. Computed from the
        // live settings so it follows them if either ceiling moves.
        //
        // The session row's own expiry is deliberately not read back: it may
        // already be gone, and this must stay one round trip on a path that is
        // answering an attack in progress.
        var jwt = _jwtSettings.CurrentValue;
        _blacklistService.BlacklistSession(
            sessionId.ToString(),
            DateTime.UtcNow + jwt.AccessTokenLifetime + jwt.ClockSkew);

        _logger.LogInformation(
            "Terminated session {SessionId}: {Reason}", sessionId, reason);
    }

    /// <inheritdoc />
    public Task<int> RevokeAllCredentialsAsync(
        Guid userId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        return RevokeCredentialsAsync(
            userId,
            exceptSessionId: null,
            exceptIdpSessionToken: null,
            revokedBy,
            reason,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RevokeCredentialsAsync(
        Guid userId,
        Guid? exceptSessionId,
        string? exceptIdpSessionToken,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        var terminated = await TerminateSessionsAsync(
            userId, exceptSessionId, revokedBy, reason, cancellationToken);

        // Session-less refresh tokens (legacy/device flows) are reachable only by
        // a blanket revocation, which would also kill the spared session's token
        // — so they are swept only when nothing is being spared.
        if (exceptSessionId is null)
        {
            await _refreshTokenRepository.RevokeAllForUserAsync(userId, revokedBy, reason, cancellationToken);

            // The session loop above blacklists one sid per row it found, which
            // misses any access token whose UserSessions row is not there to be
            // found: LoginResponseBuilder mints the token first and inserts the
            // row inside a catch that swallows failure by design, and a session
            // ended by an earlier call is already filtered out by
            // GetActiveSessionsForUserAsync. Those tokens carry a sid matching no
            // row, so no per-session blacklisting can reach them. This one is
            // keyed on the user and the instant, so it covers every access token
            // issued before now regardless of what the session table knows —
            // which is what makes "revoke everything" idempotent on the
            // access-token dimension rather than only on the refresh dimension.
            //
            // Gated on nothing being spared, for the same reason the sweep above
            // is: sparing a session means keeping its access token alive.
            _blacklistService.BlacklistAllUserTokens(userId, DateTime.UtcNow);
        }

        // The IdP SSO sessions behind the OIDC authorize flow. These live in
        // their own table with their own lifetime and are invisible to every
        // UserSessions operation, so they have to be named explicitly here.
        var idpRevoked = await RevokeIdpSessionsAsync(
            userId, exceptIdpSessionToken, cancellationToken);

        _logger.LogInformation(
            "Revoked credentials for user {UserId}: {SessionCount} sessions, {IdpSessionCount} SSO sessions. {Reason}",
            userId, terminated, idpRevoked, reason);

        return terminated;
    }

    /// <inheritdoc />
    public Task<int> RevokeIdpSessionsAsync(
        Guid userId,
        string? exceptIdpSessionToken,
        CancellationToken cancellationToken)
    {
        var exceptTokenHash = string.IsNullOrEmpty(exceptIdpSessionToken)
            ? null
            : _tokenKeyService.ComputeTokenHash(exceptIdpSessionToken);

        return _idpSessionRepository.RevokeAllForUserExceptAsync(
            userId, exceptTokenHash, cancellationToken);
    }
}
