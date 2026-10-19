using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
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
    private readonly IIdpSessionReader _idpSessionReader;
    private readonly IOptionsSnapshot<IdentityProviderSettings> _idpSettings;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CredentialRevocationService> _logger;

    /// <summary>
    /// The most reads one single logout makes. Two consecutive empty reads end it; the cap only
    /// bounds a read that keeps failing to come back empty (a session whose end keeps failing).
    /// </summary>
    private const int MaxSingleLogoutReads = 5;

    public CredentialRevocationService(
        IUserSessionRepository sessionRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IIdpSessionRepository idpSessionRepository,
        ITokenBlacklistService blacklistService,
        IRefreshTokenKeyService tokenKeyService,
        IOptionsMonitor<JwtSettings> jwtSettings,
        IIdpSessionReader idpSessionReader,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<CredentialRevocationService> logger)
    {
        _sessionRepository = sessionRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _idpSessionRepository = idpSessionRepository;
        _blacklistService = blacklistService;
        _tokenKeyService = tokenKeyService;
        _jwtSettings = jwtSettings;
        _idpSessionReader = idpSessionReader;
        _idpSettings = idpSettings;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Read per call, so a console change applies to the next sign-out (OI-155).
    /// </summary>
    private bool SingleLogoutEnabled => _idpSettings.Value.SingleLogoutEnabled;

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
        // revoke what can mint a new access token, end the row, and blacklist
        // the id so the access token already out there stops working now.
        // Ending the row alone would end nothing — the refresh token would walk
        // the session straight back in, and nothing on the refresh path reads
        // the row's end. Hence the order: a call that stops between the two
        // writes leaves a live row with dead tokens, never a dead row with a
        // live token minting under it unseen.
        await _refreshTokenRepository.RevokeBySessionIdAsync(
            sessionId, revokedBy, reason, cancellationToken);
        await _sessionRepository.TerminateAsync(sessionId, reason, cancellationToken);

        // The session row's own expiry is deliberately not read back: it may
        // already be gone, and this must stay one round trip on a path that is
        // answering an attack in progress.
        _blacklistService.BlacklistSession(sessionId.ToString(), AccessTokenAcceptanceHorizon());

        _logger.LogInformation(
            "Terminated session {SessionId}: {Reason}", sessionId, reason);
    }

    /// <inheritdoc />
    public async Task<int> TerminateApplicationSessionsAsync(
        Guid applicationId,
        Guid? userId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        // Every session either step reaches is blacklisted, each once. The ended
        // rows alone miss every session still in use whose row the expiry sweep
        // has already ended (the row's expiry is fixed at sign-in while the
        // refresh chain slides), and every session whose row was never written;
        // the live refresh token each one holds names it. Both statements return
        // only what THIS call changed, so a repeat adds nothing. Ids alone come
        // back, hence the shared horizon rather than a row's expiry.
        var blacklistedUntil = AccessTokenAcceptanceHorizon();

        // Refresh tokens first: once they are revoked nothing can mint a new
        // access token for these sessions. Their sessions are blacklisted at
        // once, so a failure while ending the rows cannot leave them out.
        var revokedSessions = userId.HasValue
            ? await _refreshTokenRepository.RevokeForUserAndApplicationAsync(
                userId.Value, applicationId, revokedBy, reason, cancellationToken)
            : await _refreshTokenRepository.RevokeAllForApplicationAsync(
                applicationId, revokedBy, reason, cancellationToken);
        BlacklistSessions(revokedSessions, blacklistedUntil);

        var ended = userId.HasValue
            ? await _sessionRepository.TerminateForUserAndApplicationAsync(
                userId.Value, applicationId, reason, cancellationToken)
            : await _sessionRepository.TerminateForApplicationAsync(
                applicationId, reason, cancellationToken);
        var endedOnly = ended.Except(revokedSessions).ToList();
        BlacklistSessions(endedOnly, blacklistedUntil);

        var killed = revokedSessions.Count + endedOnly.Count;
        _logger.LogInformation(
            "Killed {SessionCount} sessions of application {ApplicationId} (user {UserId}): {RevokedCount} with live refresh tokens revoked, {EndedCount} rows ended. {Reason}",
            killed, applicationId, userId, revokedSessions.Count, ended.Count, reason);

        return killed;
    }

    private void BlacklistSessions(IEnumerable<Guid> sessionIds, DateTime until)
    {
        foreach (var sessionId in sessionIds)
        {
            _blacklistService.BlacklistSession(sessionId.ToString(), until);
        }
    }

    /// <summary>
    /// How long a killed session's id stays blacklisted: until the last moment a
    /// token issued right now could still be accepted.
    /// </summary>
    /// <remarks>
    /// That is NOT the access-token lifetime alone: validation adds ClockSkew on
    /// top of exp, so a flat one-day horizon could lapse while a token was still
    /// being honoured — the settings ceiling for the lifetime is exactly one day,
    /// leaving no room for the skew. Computed from the live settings so it follows
    /// them if either ceiling moves.
    /// </remarks>
    private DateTime AccessTokenAcceptanceHorizon()
    {
        var jwt = _jwtSettings.CurrentValue;
        return DateTime.UtcNow + jwt.AccessTokenLifetime + jwt.ClockSkew;
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

    /// <inheritdoc />
    public async Task RevokeIdpSessionAsync(string? idpSessionToken, CancellationToken cancellationToken)
    {
        await RevokeIdpSessionForSignOutAsync(idpSessionToken, signedInUserId: null);
    }

    /// <inheritdoc />
    public async Task RevokeIdpSessionAsync(
        string? idpSessionToken, Guid signedInUserId, CancellationToken cancellationToken)
    {
        await RevokeIdpSessionForSignOutAsync(idpSessionToken, signedInUserId);
    }

    /// <summary>
    /// The browser's SSO session ends whoever it belongs to, as before OI-155. The sign-ins born
    /// from it end only when it is the signed-in user's own, or when no user is named.
    /// </summary>
    private async Task RevokeIdpSessionForSignOutAsync(string? idpSessionToken, Guid? signedInUserId)
    {
        if (string.IsNullOrEmpty(idpSessionToken))
        {
            return;
        }

        // Without the caller's token, from the lookup on (OI-155): a client that disconnects must
        // not stop the sign-out before the session, or the sign-ins born from it, have ended.
        var idpSession = await FindIdpSessionNamedByCookieAsync(
            _tokenKeyService.ComputeTokenHash(idpSessionToken), CancellationToken.None);
        if (idpSession is { IsRevoked: false })
        {
            idpSession.Revoke();
            await _idpSessionRepository.UpdateAsync(idpSession, CancellationToken.None);
        }

        if (idpSession is null)
        {
            return;
        }

        // A sign-out from a tab left open by someone else (the browser now holds another person's
        // cookie) must not end THAT person's applications.
        if (signedInUserId is { } signedIn && signedIn != idpSession.UserId)
        {
            _logger.LogInformation(
                "Sign-out of user {UserId} presented the IdP session of user {OwnerId}; its sign-ins are not ended",
                signedIn, idpSession.UserId);
            return;
        }

        // A sign-out in this browser (OI-155). Live or not: a person who signs out means the
        // browser, and chains still linked to a session that already went idle are theirs too,
        // also when a read has already ended it as idle.
        await EndSignInsBornFromIdpSessionAsync(
            idpSession.UserId, idpSession.Id, SingleLogoutTriggers.SignOut, signedInUserId ?? idpSession.UserId);
    }

    /// <summary>
    /// The SSO session a sign-out's cookie names, for the two explicit ends of a browser's session:
    /// a sign-out there, and a sign-in replacing it. The live session first, as every reader finds
    /// it. With single logout on, also a revoked one (OI-155): an earlier read, most often the
    /// authorize step of the next person's sign-in, may already have ended it as idle, and the
    /// sign-ins born from it must still end. Off, a revoked session is not looked up at all, as
    /// before. The authorize reader never comes here, so a revoked cookie still signs nobody in.
    /// </summary>
    private async Task<IdpSession?> FindIdpSessionNamedByCookieAsync(
        string tokenHash, CancellationToken cancellationToken)
    {
        var live = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (live is not null || !SingleLogoutEnabled)
        {
            return live;
        }

        return await _idpSessionRepository.GetByTokenHashIncludingRevokedAsync(tokenHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeIdpSessionByIdAsync(Guid idpSessionId, CancellationToken cancellationToken)
    {
        // Live by the one rule every reader applies (OI-154): not revoked, not past its lifetime,
        // and, for a browser session, not idle. A session that is not live signs nobody in, so
        // ending it is no news to report; the reader has already ended an idle one.
        if (!await _idpSessionReader.IsLiveAsync(idpSessionId, cancellationToken))
        {
            return false;
        }

        var idpSession = await _idpSessionRepository.GetByIdAsync(idpSessionId, cancellationToken);
        if (idpSession is not { IsRevoked: false })
        {
            return false;
        }

        idpSession.Revoke();
        await _idpSessionRepository.UpdateAsync(idpSession, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task ReplaceIdpSessionAsync(
        string? previousIdpSessionToken,
        Guid? signedInUserId,
        Guid? replacementIdpSessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(previousIdpSessionToken))
        {
            return;
        }

        var previous = await FindIdpSessionNamedByCookieAsync(
            _tokenKeyService.ComputeTokenHash(previousIdpSessionToken), cancellationToken);

        if (SingleLogoutEnabled)
        {
            if (previous is not null)
            {
                await ReplaceWithSingleLogoutAsync(previous, signedInUserId, replacementIdpSessionId, cancellationToken);
            }

            return;
        }

        if (previous is not { IsRevoked: false })
        {
            return;
        }

        // Moved BEFORE the revocation: a call that stops between the two leaves
        // the chains on a live session, never a person's own chains on a dead one.
        // And only from a session that still signs the browser in: one already
        // idle or past its maximum, merely not revoked yet, has chains that are
        // already refused, and a new sign-in must not bring them back.
        if (signedInUserId is { } userId && previous.UserId == userId &&
            replacementIdpSessionId is { } replacement && replacement != previous.Id &&
            await _idpSessionReader.IsLiveAsync(previous.Id, cancellationToken))
        {
            var moved = await _refreshTokenRepository.RelinkIdpSessionAsync(
                userId, previous.Id, replacement, cancellationToken);

            _logger.LogInformation(
                "Moved {Count} refresh tokens of user {UserId} from IdP session {PreviousIdpSessionId} to {IdpSessionId}",
                moved, userId, previous.Id, replacement);
        }

        previous.Revoke();
        await _idpSessionRepository.UpdateAsync(previous, cancellationToken);
    }

    /// <summary>
    /// <see cref="ReplaceIdpSessionAsync"/> with single logout on (OI-155). The previous session
    /// may be live, idle or past its maximum with no read having ended it yet, or already revoked:
    /// the authorize step of the new sign-in reads it first and ends it if idle, and the lookup
    /// still finds it (<see cref="FindIdpSessionNamedByCookieAsync"/>). A revoked one is not
    /// revoked again; its chains are reached all the same. Only a row the retention sweep has
    /// deleted is no longer found.
    /// </summary>
    private async Task ReplaceWithSingleLogoutAsync(
        IdpSession previous, Guid? signedInUserId, Guid? replacementIdpSessionId, CancellationToken cancellationToken)
    {
        if (signedInUserId is { } userId && previous.UserId == userId)
        {
            // The same person: nothing ends. Moved BEFORE the revocation, as in the switch-off
            // path. A platform app's own chain moves only from a session that still signs the
            // browser in (it is already refused otherwise, OI-154); an application's chain moves
            // whatever the session's state, since it never ends with an idle one (D-155-1 (a)),
            // and a later sign-out in this browser must still reach it.
            if (replacementIdpSessionId is { } replacement && replacement != previous.Id)
            {
                var moved = !previous.IsRevoked && await _idpSessionReader.IsLiveAsync(previous.Id, cancellationToken)
                    ? await _refreshTokenRepository.RelinkIdpSessionAsync(userId, previous.Id, replacement, cancellationToken)
                    : await _refreshTokenRepository.RelinkApplicationChainsAsync(userId, previous.Id, replacement, cancellationToken);

                _logger.LogInformation(
                    "Moved {Count} refresh tokens of user {UserId} from IdP session {PreviousIdpSessionId} to {IdpSessionId}",
                    moved, userId, previous.Id, replacement);
            }
        }

        if (!previous.IsRevoked)
        {
            previous.Revoke();
            await _idpSessionRepository.UpdateAsync(previous, cancellationToken);
        }

        // Another person signed in on this browser: the previous person's sign-ins born here end,
        // live or idle. An unknown person ends nothing more: it cannot be told from the same one.
        if (signedInUserId is { } otherUserId && previous.UserId != otherUserId)
        {
            await EndSignInsBornFromIdpSessionAsync(
                previous.UserId, previous.Id, SingleLogoutTriggers.OtherUserSignedIn, otherUserId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EndedSignIn>> EndSignInsBornFromIdpSessionAsync(
        Guid userId, Guid idpSessionId, string trigger, Guid? endedBy)
    {
        if (!SingleLogoutEnabled)
        {
            return [];
        }

        // Each session once, and each session-less token once, across every pass.
        var ended = new List<EndedSignIn>();
        var endedSessions = new HashSet<Guid>();
        var revokedTokens = new HashSet<Guid>();

        // Why the read repeats (the race with a refresh, T11). A refresh that rotates one of these
        // tokens either loses, because a pass revoked the token first and its conditional update
        // then matches nothing (RefreshTokenRepository.TryRotateAsync, "[RevokedAt] IS NULL"), or
        // wins, holding the token's row lock until its replacement (same session, same IdP
        // session) is committed. A statement of this cascade that meets that lock waits for the
        // commit and may still miss the replacement, which can sit where the statement already
        // looked. The next read starts after that commit and finds it. So the cascade ends only
        // after two reads in a row found nothing: the first may have waited on a rotation, the
        // second sees its replacement.
        var emptyReadsInARow = 0;
        var successfulReads = 0;
        for (var read = 0; read < MaxSingleLogoutReads && emptyReadsInARow < 2; read++)
        {
            IReadOnlyList<RefreshToken> live;
            try
            {
                live = await _refreshTokenRepository.GetLiveByIdpSessionAsync(
                    userId, idpSessionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // A failed read proves nothing, so it never counts as an empty one; the next read
                // tries again, within the cap.
                _logger.LogWarning(ex,
                    "Single logout of IdP session {IdpSessionId} (user {UserId}) could not read the sign-ins born from it; {EndedCount} ended so far",
                    idpSessionId, userId, ended.Count);
                emptyReadsInARow = 0;
                continue;
            }

            successfulReads++;
            emptyReadsInARow = live.Count == 0 ? emptyReadsInARow + 1 : 0;

            foreach (var session in live.Where(t => t.SessionId.HasValue).GroupBy(t => t.SessionId!.Value))
            {
                if (await EndSessionAsync(session.Key, endedBy, idpSessionId, userId) && endedSessions.Add(session.Key))
                {
                    ended.Add(new EndedSignIn(session.Key, session.First().ApplicationId));
                }
            }

            foreach (var token in live.Where(t => !t.SessionId.HasValue))
            {
                // A token issued outside any session row has nothing else to end.
                token.Revoke(endedBy, TokenRevocationReasons.SingleLogout);
                if (await TryEndAsync(() => _refreshTokenRepository.UpdateAsync(token, CancellationToken.None),
                        idpSessionId, userId) &&
                    revokedTokens.Add(token.Id))
                {
                    ended.Add(new EndedSignIn(null, token.ApplicationId));
                }
            }
        }

        if (successfulReads == 0)
        {
            _logger.LogError(
                "Single logout of IdP session {IdpSessionId} (user {UserId}) ended nothing: no read of the sign-ins born from it succeeded",
                idpSessionId, userId);
        }
        else if (emptyReadsInARow < 2)
        {
            _logger.LogWarning(
                "Single logout of IdP session {IdpSessionId} (user {UserId}) stopped before two empty reads; a sign-in born from it may still be live",
                idpSessionId, userId);
        }

        if (ended.Count == 0)
        {
            return ended;
        }

        _logger.LogInformation(
            "Single logout of IdP session {IdpSessionId} (user {UserId}, {Trigger}): {EndedCount} sign-ins ended",
            idpSessionId, userId, trigger, ended.Count);

        try
        {
            await _publisher.Publish(
                new SingleLogoutCompletedEvent(
                    Guid.NewGuid(), userId, idpSessionId, ended.Count, trigger, endedBy, _timeProvider.GetUtcNow()),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The sign-ins have ended; only the record of it failed. The sign-out still succeeds.
            _logger.LogWarning(ex,
                "Single logout of IdP session {IdpSessionId} (user {UserId}) ended {EndedCount} sign-ins, but publishing its event failed",
                idpSessionId, userId, ended.Count);
        }

        return ended;
    }

    /// <summary>
    /// The three moves of <see cref="TerminateSessionAsync"/> for one session of a single logout,
    /// each kept apart: the session counts as ended once its refresh tokens are revoked, since
    /// nothing can then mint for it, even when ending its row fails afterwards. Its id is
    /// blacklisted in every case, because a session whose tokens are gone is not read again.
    /// </summary>
    /// <returns>Whether the session's refresh tokens were revoked.</returns>
    private async Task<bool> EndSessionAsync(Guid sessionId, Guid? endedBy, Guid idpSessionId, Guid userId)
    {
        var tokensRevoked = await TryEndAsync(
            () => _refreshTokenRepository.RevokeBySessionIdAsync(
                sessionId, endedBy, TokenRevocationReasons.SingleLogout, CancellationToken.None),
            idpSessionId, userId);

        if (tokensRevoked)
        {
            try
            {
                await _sessionRepository.TerminateAsync(sessionId, TokenRevocationReasons.SingleLogout, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Single logout of IdP session {IdpSessionId} (user {UserId}) revoked the tokens of session {SessionId} but could not end its row",
                    idpSessionId, userId, sessionId);
            }
        }

        _blacklistService.BlacklistSession(sessionId.ToString(), AccessTokenAcceptanceHorizon());
        return tokensRevoked;
    }

    /// <summary>
    /// One end inside a single logout: a failure is logged at Warning and reported as false, so the
    /// other sign-ins still end and the sign-out never fails because of one of them.
    /// </summary>
    private async Task<bool> TryEndAsync(Func<Task> end, Guid idpSessionId, Guid userId)
    {
        try
        {
            await end();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Single logout of IdP session {IdpSessionId} (user {UserId}) failed to end one sign-in; the others still end",
                idpSessionId, userId);
            return false;
        }
    }
}
