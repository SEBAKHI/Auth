using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.RefreshToken;

/// <summary>
/// Handler for the refresh token command.
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ErrorOr<TokenResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenClaimsResolver _tokenClaimsResolver;
    private readonly IPlatformMfaPolicy _platformMfaPolicy;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenClaimsResolver tokenClaimsResolver,
        IPlatformMfaPolicy platformMfaPolicy,
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IUserSessionRepository sessionRepository,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenClaimsResolver = tokenClaimsResolver;
        _platformMfaPolicy = platformMfaPolicy;
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _sessionRepository = sessionRepository;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<TokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Compute HMAC-SHA256 hash of the incoming token for lookup
        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(request.RefreshToken);
        var presentedToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (presentedToken == null)
        {
            _logger.LogWarning("Refresh token not found. IP: {IpAddress}", request.IpAddress);
            return AuthErrors.RefreshTokenNotFound;
        }

        // A refresh token is bound to the client it was issued to (RFC 6749 §6).
        // A client_id naming any other client, one this server does not know, one
        // switched off, or any client at all for a first-party token, is refused
        // here: before anything is revoked, rotated or counted as reuse. A request
        // that is wrong about who it is proves nothing about who holds the token.
        // Without a client_id the refresh goes on exactly as before.
        var clientConfirmed = false;
        if (request.ClientId is not null)
        {
            if (!await IsIssuedToClientAsync(presentedToken, request.ClientId, cancellationToken))
            {
                _logger.LogWarning(
                    "Refresh token of user {UserId}, application {ApplicationId}, presented with mismatched client {ClientId}. IP: {IpAddress}",
                    presentedToken.UserId, presentedToken.ApplicationId, request.ClientId, request.IpAddress);
                return AuthErrors.InvalidClient;
            }

            clientConfirmed = true;
        }

        // The live token this request rotates. Normally the presented one; within
        // the replay grace window, the replacement a lost response never delivered.
        var storedToken = presentedToken;
        var answeredFromGrace = false;

        // Check if token is revoked
        if (presentedToken.IsRevoked)
        {
            var graceReplacement = await FindReplayGraceReplacementAsync(
                request, presentedToken, clientConfirmed, cancellationToken);
            if (graceReplacement is not null)
            {
                storedToken = graceReplacement;
                answeredFromGrace = true;
            }
        }

        if (storedToken.IsRevoked)
        {
            // A token that a bulk revocation killed is NOT evidence of theft.
            // Its holder never spent it - this is the account owner's other
            // device finding out that its session was ended elsewhere, whether
            // by a reuse cascade, a "sign out everywhere", a lockout or a
            // deletion. Answer it as what it is: the session is over, sign in
            // again.
            //
            // Treating it as a fresh attack is what made one incident
            // self-perpetuating. Every innocent device that refreshed after a
            // mass revocation triggered ANOTHER mass revocation, which killed
            // whatever session the user had just signed back in to - so signing
            // in on one device knocked out the other, forever, with an alarming
            // e-mail each time. Reproduced end to end against a live API before
            // this branch existed.
            if (storedToken.WasTerminatedInBulk)
            {
                _logger.LogInformation(
                    "Refresh rejected for user {UserId}: this session was already ended ({Reason}). IP: {IpAddress}",
                    storedToken.UserId, storedToken.ReasonRevoked, request.IpAddress);

                return AuthErrors.RefreshTokenRevoked;
            }

            // The family rule. A rotated token whose session no longer holds a
            // single live refresh token belongs to a family that is dead: the
            // first detection revoked every token of the user, and a sign-out or
            // a lockout revoked that session's. Presenting it again gives a thief
            // nothing, so it is answered like a bulk revocation, with no second
            // cascade and no second alarm. Without this, one stolen,
            // already-rotated token is a button that signs the user out of
            // everything, the sign-in page included, each time it is pressed.
            //
            // Decided from the refresh tokens, never from the session row. The
            // row's expiry slides with the refresh chain only through a
            // best-effort write, and rows the expiry sweep ended before it did
            // stay ended, so a session still in use can have an ended row; an
            // ended row with a live token in its family is the very theft this
            // branch exists to catch. A token with no session, or whose family
            // cannot be read, still cascades: the safe side.
            if (await IsFamilyDeadAsync(storedToken, cancellationToken))
            {
                _logger.LogInformation(
                    "Refresh rejected for user {UserId}: a rotated token of session {SessionId}, which holds no live refresh token any more, was presented again. IP: {IpAddress}",
                    storedToken.UserId, storedToken.SessionId, request.IpAddress);

                return AuthErrors.RefreshTokenRevoked;
            }

            return await RevokeForReuseAsync(storedToken.UserId, request.IpAddress);
        }

        // Check if token is expired
        if (storedToken.IsExpired())
        {
            _logger.LogWarning("Refresh token expired for user {UserId}. IP: {IpAddress}",
                storedToken.UserId, request.IpAddress);
            return AuthErrors.RefreshTokenExpired;
        }

        // Get the user
        var user = await _userRepository.GetByIdAsync(storedToken.UserId, cancellationToken);
        if (user == null)
        {
            _logger.LogError("User {UserId} not found for valid refresh token", storedToken.UserId);
            return UserErrors.NotFound(storedToken.UserId);
        }

        // Check the user may still be issued credentials. Not IsLockedOut(),
        // which only ever matched Locked and let a deactivated account renew
        // forever.
        if (!user.CanRenewCredentials())
        {
            await _refreshTokenRepository.RevokeAllForUserAsync(
                user.Id,
                null, // revokedBy - system action
                "User account locked",
                cancellationToken);

            return UserErrors.AccountLocked;
        }

        // A token issued to a specific app (OAuth flow) carries that app on the
        // refresh token; re-mint the same per-app audience so the refreshed token
        // stays valid only for that app. Direct first-party logins have no
        // ApplicationId and keep the platform default audience.
        // A missing (soft-deleted) or inactive app must NOT fall back to the
        // platform audience: that would silently escalate an app-scoped token
        // into one the platform API itself accepts.
        string? audience = null;
        string? scope = null;
        if (storedToken.ApplicationId.HasValue)
        {
            var application = await _applicationRepository.GetByIdAsync(
                storedToken.ApplicationId.Value, cancellationToken);
            if (application is null || !application.IsActive)
            {
                _logger.LogWarning(
                    "Refresh rejected: application {ApplicationId} is deleted or inactive",
                    storedToken.ApplicationId);
                return ApplicationErrors.ApplicationInactive;
            }

            // Entitlement is re-checked on every refresh, so withdrawing an
            // invitation takes effect within one access-token lifetime instead
            // of one refresh-token lifetime.
            if (!await _applicationAccessRepository.IsUserEntitledAsync(
                    user.Id, application.Id, cancellationToken))
            {
                // Revoke THIS token only. Losing access to one application must
                // not sign the user out of the others, so no bulk revocation
                // here. The reason is not "Rotated", so WasTerminatedInBulk is
                // true and re-presenting this token answers "session ended"
                // rather than triggering the theft cascade.
                storedToken.Revoke(
                    revokedBy: null, // system action, not an administrator acting now
                    reason: TokenRevocationReasons.ApplicationAccessRevoked);
                await _refreshTokenRepository.UpdateAsync(storedToken, cancellationToken);

                _logger.LogWarning(
                    "Refresh rejected: user {UserId} is no longer entitled to application {ApplicationId}",
                    user.Id, application.Id);

                return ApplicationErrors.AccessDenied;
            }

            audience = application.Code;

            // The stored grant, narrowed to what the application is allowed now:
            // a scope an administrator removed is gone from this refresh on, and
            // a scope added since the sign-in is not picked up (that takes a new
            // authorize). With rotation off the stored row keeps the original
            // grant, so a scope removed and allowed again comes back. A platform
            // token has no application and no scope.
            scope = storedToken.NarrowGrant(application.AllowedScopes).Value;
        }

        // Claims are resolved for the audience this token is scoped to, so a
        // role that belongs to another application cannot ride along.
        var resolved = await _tokenClaimsResolver.ResolveAsync(
            user.Id, storedToken.ApplicationId, cancellationToken);

        // The session row, read BEFORE the mint: what the session proved and when
        // it started are what this token's amr, auth_time and the platform-
        // administrator decision are made from. Every rotated, sibling and grace
        // token of the session shares it through SessionId.
        var session = await ReadSessionAsync(storedToken, cancellationToken);
        var (methods, startedAt) = SessionAuthentication(storedToken, session);

        // The decision every sign-in makes, made again: a factor removed since the
        // sign-in withholds the authority from this refresh on, and a step-up
        // since restores it. An application token is left as it is.
        var mfa = await _platformMfaPolicy.EvaluateAsync(
            user.Id, storedToken.ApplicationId, resolved, methods, cancellationToken);
        var claims = mfa.Claims;

        var authentication = storedToken.ApplicationId is null
            ? new AccessTokenAuthentication(methods, startedAt, mfa.Requirement)
            : AccessTokenAuthentication.Unrecorded;

        // Generate new access token, carrying the stable session id forward so
        // the access token's "sid" stays constant across refreshes.
        // The organization claims too: minted at sign-in only, org_id would
        // vanish from the first refreshed token.
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user, claims.Permissions, claims.RoleCodes, authentication, storedToken.SessionId,
            claims.OrganizationPermissions, audience, scope, claims.Organization);

        string newRefreshToken;
        int refreshExpiresIn;
        DateTime refreshExpiresAt;

        // Rotate refresh token if enabled
        if (_jwtSettings.RotateRefreshTokens)
        {
            var newToken = _jwtTokenService.GenerateRefreshToken();
            var newTokenHash = _refreshTokenKeyService.ComputeTokenHash(newToken);
            var newJwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
            newRefreshToken = newToken;

            // Create new token (only hash is stored, not plain token). It carries
            // the narrowed grant, and so does a race sibling built from it.
            var newRefreshTokenEntity = RefreshTokenEntity.Create(
                user.Id,
                newTokenHash,
                newJwtId,
                storedToken.ApplicationId,
                _jwtSettings.RefreshTokenLifetime,
                request.IpAddress,
                storedToken.DeviceInfo,
                storedToken.SessionId,
                scope);

            // Revoke the rotated token and create its replacement in one
            // transaction. A grace answer names no replacement: that is what
            // makes it single-use (a replacement-less rotated token is never
            // eligible again, so the next presentation of it is reuse).
            storedToken.Revoke(
                user.Id,
                TokenRevocationReasons.Rotated,
                answeredFromGrace ? null : newTokenHash);

            var won = await _refreshTokenRepository.TryRotateAsync(
                storedToken, newRefreshTokenEntity, cancellationToken);

            if (!won)
            {
                var refusal = await ResolveLostRotationAsync(
                    storedToken, newRefreshTokenEntity, answeredFromGrace, request.IpAddress, cancellationToken);
                if (refusal is { } error)
                {
                    return error;
                }
            }
            else if (answeredFromGrace && storedToken.ApplicationId is { } applicationId)
            {
                _logger.LogWarning(
                    "RefreshToken.ApplicationReplayGraceUsed: a just-rotated refresh token of application {ApplicationId} was presented again by that application within its grace window and answered once for user {UserId}, session {SessionId}. IP: {IpAddress}",
                    applicationId, user.Id, storedToken.SessionId, request.IpAddress);
            }
            else if (answeredFromGrace)
            {
                _logger.LogWarning(
                    "RefreshToken.ReplayGraceUsed: a just-rotated refresh token was presented again from the first-party cookie within the grace window and answered once for user {UserId}, session {SessionId}. IP: {IpAddress}",
                    user.Id, storedToken.SessionId, request.IpAddress);
            }

            refreshExpiresIn = (int)_jwtSettings.RefreshTokenLifetime.TotalSeconds;
            refreshExpiresAt = newRefreshTokenEntity.ExpiresAt;

            _logger.LogDebug("Rotated refresh token for user {UserId}", user.Id);
        }
        else
        {
            // Return the same refresh token
            newRefreshToken = request.RefreshToken;
            refreshExpiresIn = (int)(storedToken.ExpiresAt - DateTime.UtcNow).TotalSeconds;
            refreshExpiresAt = storedToken.ExpiresAt;
        }

        // The session row lives as long as the refresh token this response hands
        // out, so a session in use never reaches the expiry sweep (OI-103). After
        // the rotation decision, because that is what fixes the expiry; and only
        // once a token is really handed out.
        if (session is { IsActive: true })
        {
            await TouchSessionAsync(storedToken, refreshExpiresAt);
        }

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = (int)_jwtSettings.AccessTokenLifetime.TotalSeconds,
            RefreshExpiresIn = refreshExpiresIn,
            Scope = scope
        };
    }

    /// <summary>
    /// The token's session row, or null when the token names none, the row is
    /// missing, or reading it failed. Never an error to the client: a session row
    /// is written on a path allowed to fail, and a refresh must not depend on it.
    /// </summary>
    private async Task<UserSession?> ReadSessionAsync(
        RefreshTokenEntity storedToken,
        CancellationToken cancellationToken)
    {
        if (storedToken.SessionId is not { } sessionId)
        {
            return null;
        }

        try
        {
            return await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to read session {SessionId} for user {UserId}; its authentication methods are unknown for this refresh",
                sessionId, storedToken.UserId);
            return null;
        }
    }

    /// <summary>
    /// Records the session's activity and slides its expiry to
    /// <paramref name="expiresAt"/>, the expiry of the refresh token this response
    /// hands out. One guarded write: it never revives a session a sign-out ended
    /// after the read (OI-97), and it never moves the expiry backwards. Best-effort,
    /// like every session-row write on this path: a failure is a warning, never an
    /// error to the client.
    /// </summary>
    /// <remarks>
    /// It runs without the request's cancellation. The token it follows is already
    /// committed, so the row must follow it whether or not the client stays for the
    /// answer; and a committed rotation must not end in an error because of
    /// bookkeeping. A cancellation surfacing here is therefore a timeout, and a
    /// failure like any other.
    /// </remarks>
    private async Task TouchSessionAsync(RefreshTokenEntity storedToken, DateTime expiresAt)
    {
        if (storedToken.SessionId is not { } sessionId)
        {
            return;
        }

        try
        {
            await _sessionRepository.TouchOnRefreshAsync(
                sessionId, storedToken.UserId, DateTime.UtcNow, expiresAt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update session activity for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// True only when the token names a session that holds no refresh token
    /// still live (neither revoked nor expired). No session, or a read that
    /// failed, answers false, so the caller falls back to treating the token as
    /// theft: the safe side.
    /// </summary>
    private async Task<bool> IsFamilyDeadAsync(
        RefreshTokenEntity token,
        CancellationToken cancellationToken)
    {
        if (token.SessionId is not { } sessionId)
        {
            return false;
        }

        try
        {
            return !await _refreshTokenRepository.HasLiveTokenInSessionAsync(sessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to read the refresh tokens of session {SessionId} for user {UserId}; the replayed token is treated as reuse",
                sessionId, token.UserId);
            return false;
        }
    }

    /// <summary>
    /// What the session proved and when it started, for a platform token. A token
    /// with no live session row behind it proved nothing anyone can read back:
    /// <see cref="AuthenticationMethods.Unknown"/>, which emits no amr or auth_time
    /// and, for a platform administrator under enforcement, asks for a new sign-in.
    /// </summary>
    private (AuthenticationMethods Methods, DateTimeOffset? StartedAt) SessionAuthentication(
        RefreshTokenEntity storedToken,
        UserSession? session)
    {
        if (storedToken.ApplicationId is not null)
        {
            return (AuthenticationMethods.Unknown, null);
        }

        if (session is not { IsActive: true } || session.UserId != storedToken.UserId)
        {
            _logger.LogWarning(
                "No live session row {SessionId} behind the refresh of user {UserId}; its authentication methods are unknown",
                storedToken.SessionId, storedToken.UserId);
            return (AuthenticationMethods.Unknown, null);
        }

        var methods = session.Methods;

        // Dapper hands DATETIME2 back with no Kind; the column holds UTC.
        return methods.IsUnknown
            ? (methods, null)
            : (methods, new DateTimeOffset(DateTime.SpecifyKind(session.CreatedAt, DateTimeKind.Utc)));
    }

    /// <summary>
    /// Whether <paramref name="clientId"/> names the application
    /// <paramref name="token"/> was issued to, and that application is active. The
    /// identifiers are compared, never the strings, as the code exchange does. A
    /// first-party token belongs to no client, so no client_id names it.
    /// </summary>
    private async Task<bool> IsIssuedToClientAsync(
        RefreshTokenEntity token,
        string clientId,
        CancellationToken cancellationToken)
    {
        if (token.ApplicationId is not { } applicationId)
        {
            return false;
        }

        var application = await _applicationRepository.GetByCodeAsync(clientId, cancellationToken);
        return application is { IsActive: true } && application.Id == applicationId;
    }

    /// <summary>
    /// The live replacement of a just-rotated token, when the presentation may be
    /// the same client whose rotation response was lost; <c>null</c> otherwise.
    /// <para>
    /// Two kinds of presentation qualify, each with its own window:
    /// a first-party token from the first-party cookie (no script can read it, so a
    /// second holder needs the browser's files, not an XSS), within
    /// <c>Jwt:RefreshReplayGraceSeconds</c>; and an application's token sent by that
    /// same application, named by its client_id, within
    /// <c>Jwt:ApplicationRefreshReplayGraceSeconds</c> (0 turns it off). Either only
    /// while rotation is on, and only while the replacement is still live. Anything
    /// else falls through to the reuse detection exactly as before.
    /// </para>
    /// </summary>
    private async Task<RefreshTokenEntity?> FindReplayGraceReplacementAsync(
        RefreshTokenCommand request,
        RefreshTokenEntity presented,
        bool clientConfirmed,
        CancellationToken cancellationToken)
    {
        if (!_jwtSettings.RotateRefreshTokens ||
            ReplayGraceWindow(request, presented, clientConfirmed) is not { } grace ||
            !presented.IsWithinReplayGrace(grace, DateTime.UtcNow))
        {
            return null;
        }

        var replacement = await _refreshTokenRepository.GetByTokenHashAsync(
            presented.ReplacedByTokenHash!, cancellationToken);

        return replacement is { IsRevoked: false } &&
               !replacement.IsExpired() &&
               replacement.UserId == presented.UserId
            ? replacement
            : null;
    }

    /// <summary>
    /// The grace window this presentation may be answered in, or null for none. A
    /// first-party token never gets the application window, and an application's
    /// token never gets the cookie window.
    /// </summary>
    private TimeSpan? ReplayGraceWindow(
        RefreshTokenCommand request,
        RefreshTokenEntity presented,
        bool clientConfirmed)
    {
        // Only first-party sessions (no application) are ever delivered as the
        // cookie. The channel is what the request claims, and a non-browser client
        // can claim it; an application's token presented "from the cookie" is
        // therefore not given the window its holder could never have needed.
        if (presented.ApplicationId is null)
        {
            return request.ReplayGraceEligible ? _jwtSettings.RefreshReplayGrace : null;
        }

        // An application's token, only when the request names that application
        // (RFC 6749 §6). The client_id of a public client is no secret, so this
        // stops another application, not a thief of this one; the window and the
        // single use are what bound that thief.
        return clientConfirmed && _jwtSettings.ApplicationRefreshReplayGrace > TimeSpan.Zero
            ? _jwtSettings.ApplicationRefreshReplayGrace
            : null;
    }

    /// <summary>
    /// Decides what a request that lost the rotation race gets. <c>null</c> means
    /// "succeed": the request is answered with a sibling token.
    /// <list type="bullet">
    /// <item>A grace answer that lost: the replacement was spent by someone else in
    /// the meantime, so two parties hold this chain. Reuse detection, as today.</item>
    /// <item>The token was ended in bulk while this request was in flight (a sign-out,
    /// a lockout): the session is over. Minting a sibling here would outlive it.</item>
    /// <item>A normal rotation that lost to a grace answer: the winner retried the
    /// previous token while this request presented the current one, so two parties
    /// hold this chain. Reuse detection, as when a grace answer loses.</item>
    /// <item>Otherwise another request of the same holder rotated it a moment
    /// earlier — concurrency, not theft. It is answered as it always was: with a
    /// token of its own in the same session, and nothing revoked.</item>
    /// </list>
    /// </summary>
    private async Task<Error?> ResolveLostRotationAsync(
        RefreshTokenEntity lost,
        RefreshTokenEntity sibling,
        bool answeredFromGrace,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // The lost token as the winner left it, read once for both questions.
        var current = await _refreshTokenRepository.GetByIdAsync(lost.Id, cancellationToken);

        if (await SessionEndedInFlightAsync(current, cancellationToken))
        {
            _logger.LogInformation(
                "Refresh for user {UserId} lost its rotation to a session end that happened in flight. IP: {IpAddress}",
                lost.UserId, ipAddress);
            return AuthErrors.RefreshTokenRevoked;
        }

        if (answeredFromGrace || (current is not null && WasSpentByGraceAnswer(current)))
        {
            return await RevokeForReuseAsync(lost.UserId, ipAddress);
        }

        await _refreshTokenRepository.CreateAsync(sibling, cancellationToken);

        _logger.LogWarning(
            "RefreshToken.ConcurrentRotation: two refreshes of one token raced for user {UserId}, session {SessionId}; the later one was answered with a sibling token. IP: {IpAddress}",
            lost.UserId, lost.SessionId, ipAddress);

        return null;
    }

    /// <summary>
    /// Whether the session of a token that just lost its rotation was ended while
    /// the request was in flight. The lost token itself is no evidence: the winner
    /// rotated it, and a sign-out or lockout that came after revokes only what was
    /// still live — the winner's replacement. So the replacement is followed too.
    /// A lost token or a replacement that cannot be found counts as ended: the safe
    /// side is to mint nothing.
    /// </summary>
    /// <param name="current">The lost token re-read after the rotation was lost.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<bool> SessionEndedInFlightAsync(
        RefreshTokenEntity? current,
        CancellationToken cancellationToken)
    {
        if (current is null || current.WasTerminatedInBulk)
        {
            return true;
        }

        if (string.IsNullOrEmpty(current.ReplacedByTokenHash))
        {
            return false;
        }

        var replacement = await _refreshTokenRepository.GetByTokenHashAsync(
            current.ReplacedByTokenHash, cancellationToken);
        return replacement is null || replacement.WasTerminatedInBulk;
    }

    /// <summary>
    /// Whether a grace answer spent this token: rotated, with no successor named.
    /// Only a grace answer writes that; every other rotation names its replacement.
    /// </summary>
    private static bool WasSpentByGraceAnswer(RefreshTokenEntity token) =>
        string.Equals(token.ReasonRevoked, TokenRevocationReasons.Rotated, StringComparison.Ordinal)
        && string.IsNullOrEmpty(token.ReplacedByTokenHash);

    /// <summary>
    /// A rotated token presented a second time: two parties hold it. Every
    /// credential of the user is revoked and the owner is told, once per incident.
    /// </summary>
    /// <remarks>
    /// None of it runs on the request's cancellation. The count is the first
    /// write and it spends the incident's one notice: a count committed and then
    /// abandoned, or a wipe or a notice cut short because the client went away,
    /// would never run again for this incident, and going away is what a thief
    /// would choose.
    /// </remarks>
    private async Task<Error> RevokeForReuseAsync(Guid userId, string? ipAddress)
    {
        _logger.LogWarning(
            "Attempted reuse of revoked refresh token for user {UserId}. Revoking all tokens. IP: {IpAddress}",
            userId, ipAddress);

        // Counted here, before the wipe below sweeps the same rows again: this
        // count is what limits the owner's notice to one per incident. The stored
        // procedure behind it also ends every session row of the user.
        var revokedCount = await _refreshTokenRepository.RevokeAllForUserAsync(
            userId,
            null, // revokedBy - system action
            TokenRevocationReasons.RefreshTokenReuse,
            CancellationToken.None);

        await RevokeRemainingCredentialsAsync(userId);

        await NotifyReuseDetectedAsync(userId, revokedCount, ipAddress, CancellationToken.None);

        return AuthErrors.TokenRevoked;
    }

    /// <summary>
    /// The rest of the lock-out every compromise path makes: every access token
    /// issued before now is refused through the user-wide blacklist entry (held
    /// one hour, X04), and the sign-in page's SSO sessions are revoked. The
    /// session rows themselves were already ended by the count's stored
    /// procedure, so no session id is blacklisted here: the user-wide entry is
    /// what stops the access tokens already out. Revoking the refresh tokens
    /// alone left those working until they expired, and the SSO cookie able to
    /// mint new ones.
    ///
    /// Nothing here may propagate. The refresh tokens are already revoked, so
    /// neither holder can renew; turning this 403 into a 500 would hide that
    /// from the legitimate client and change nothing for the thief. The failure
    /// is logged for an operator to finish by hand. It runs without the
    /// request's cancellation (see the caller), so a cancellation surfacing here
    /// is a timeout, and a failure like any other.
    /// </summary>
    private async Task RevokeRemainingCredentialsAsync(Guid userId)
    {
        try
        {
            await _credentialRevocation.RevokeAllCredentialsAsync(
                userId,
                revokedBy: null, // a system reaction, not an administrator acting now
                TokenRevocationReasons.RefreshTokenReuse,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Refresh-token reuse for user {UserId}: the refresh tokens are revoked, but ending the sessions, access tokens and SSO sessions failed",
                userId);
        }
    }

    /// <summary>
    /// Tells the account owner that every one of their sessions was revoked -
    /// but only when the revocation actually took a live session away.
    ///
    /// One incident produces many detections. The mass revocation kills the
    /// tokens held by every other tab and device, and each of those reports
    /// reuse in turn on its next refresh, so the warning can appear dozens of
    /// times for a single event. Only the first of them finds anything live to
    /// revoke; gating on the count therefore yields exactly one notice per
    /// incident, with no timer and no rate-limit table to keep correct.
    ///
    /// Nothing here may propagate. The revocation has already committed, and
    /// turning a clean 403 into a 500 because an email could not be raised
    /// would be strictly worse for the caller.
    /// </summary>
    private async Task NotifyReuseDetectedAsync(
        Guid userId,
        int revokedCount,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (revokedCount <= 0)
        {
            return;
        }

        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

            // A hard-deleted account can still have a lingering revoked token
            // pointed at it. There is then no address left to write to, and no
            // owner left to warn.
            if (user is null)
            {
                return;
            }

            await _publisher.Publish(
                new RefreshTokenReuseDetectedEvent(
                    user.Id,
                    user.Email,
                    user.DisplayName ?? user.FirstName,
                    ipAddress,
                    DateTime.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to raise the refresh-token reuse notice for user {UserId}", userId);
        }
    }
}
