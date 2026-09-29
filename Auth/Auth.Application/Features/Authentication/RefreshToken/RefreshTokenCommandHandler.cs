using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
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
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenClaimsResolver tokenClaimsResolver,
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IUserSessionRepository sessionRepository,
        IPublisher publisher,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenClaimsResolver = tokenClaimsResolver;
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _sessionRepository = sessionRepository;
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

        // The live token this request rotates. Normally the presented one; within
        // the replay grace window, the replacement a lost response never delivered.
        var storedToken = presentedToken;
        var answeredFromGrace = false;

        // Check if token is revoked
        if (presentedToken.IsRevoked)
        {
            var graceReplacement = await FindReplayGraceReplacementAsync(request, presentedToken, cancellationToken);
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

            return await RevokeForReuseAsync(storedToken.UserId, request.IpAddress, cancellationToken);
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
        }

        // Claims are resolved for the audience this token is scoped to, so a
        // role that belongs to another application cannot ride along.
        var claims = await _tokenClaimsResolver.ResolveAsync(
            user.Id, storedToken.ApplicationId, cancellationToken);

        // Generate new access token, carrying the stable session id forward so
        // the access token's "sid" stays constant across refreshes.
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user, claims.Permissions, claims.RoleCodes, storedToken.SessionId,
            claims.OrganizationPermissions, audience);

        // Keep the session's last-activity timestamp fresh (best-effort).
        if (storedToken.SessionId.HasValue)
        {
            try
            {
                var session = await _sessionRepository.GetByIdAsync(storedToken.SessionId.Value, cancellationToken);
                if (session is { IsActive: true })
                {
                    session.RecordActivity();
                    await _sessionRepository.UpdateAsync(session, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update session activity for session {SessionId}", storedToken.SessionId);
            }
        }

        string newRefreshToken;
        int refreshExpiresIn;

        // Rotate refresh token if enabled
        if (_jwtSettings.RotateRefreshTokens)
        {
            var newToken = _jwtTokenService.GenerateRefreshToken();
            var newTokenHash = _refreshTokenKeyService.ComputeTokenHash(newToken);
            var newJwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
            newRefreshToken = newToken;

            // Create new token (only hash is stored, not plain token)
            var newRefreshTokenEntity = RefreshTokenEntity.Create(
                user.Id,
                newTokenHash,
                newJwtId,
                storedToken.ApplicationId,
                _jwtSettings.RefreshTokenLifetime,
                request.IpAddress,
                storedToken.DeviceInfo,
                storedToken.SessionId);

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
            else if (answeredFromGrace)
            {
                _logger.LogWarning(
                    "RefreshToken.ReplayGraceUsed: a just-rotated refresh token was presented again from the first-party cookie within the grace window and answered once for user {UserId}, session {SessionId}. IP: {IpAddress}",
                    user.Id, storedToken.SessionId, request.IpAddress);
            }

            refreshExpiresIn = (int)_jwtSettings.RefreshTokenLifetime.TotalSeconds;

            _logger.LogDebug("Rotated refresh token for user {UserId}", user.Id);
        }
        else
        {
            // Return the same refresh token
            newRefreshToken = request.RefreshToken;
            refreshExpiresIn = (int)(storedToken.ExpiresAt - DateTime.UtcNow).TotalSeconds;
        }

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = (int)_jwtSettings.AccessTokenLifetime.TotalSeconds,
            RefreshExpiresIn = refreshExpiresIn
        };
    }

    /// <summary>
    /// The live replacement of a just-rotated token, when the presentation may be
    /// the same browser whose rotation response was lost; <c>null</c> otherwise.
    /// <para>
    /// Only a token from the first-party cookie qualifies (no script can read it,
    /// so a second holder needs the browser's files, not an XSS), only while
    /// rotation is on, only within the grace window, and only while the
    /// replacement is still live. Anything else falls through to the reuse
    /// detection exactly as before.
    /// </para>
    /// </summary>
    private async Task<RefreshTokenEntity?> FindReplayGraceReplacementAsync(
        RefreshTokenCommand request,
        RefreshTokenEntity presented,
        CancellationToken cancellationToken)
    {
        // Only first-party sessions (no application) are ever delivered as the
        // cookie. The channel is what the request claims, and a non-browser client
        // can claim it; an application's token presented "from the cookie" is
        // therefore not given the window its holder could never have needed.
        if (!request.ReplayGraceEligible ||
            presented.ApplicationId is not null ||
            !_jwtSettings.RotateRefreshTokens ||
            !presented.IsWithinReplayGrace(_jwtSettings.RefreshReplayGrace, DateTime.UtcNow))
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
    /// Decides what a request that lost the rotation race gets. <c>null</c> means
    /// "succeed": the request is answered with a sibling token.
    /// <list type="bullet">
    /// <item>A grace answer that lost: the replacement was spent by someone else in
    /// the meantime, so two parties hold this chain. Reuse detection, as today.</item>
    /// <item>The token was ended in bulk while this request was in flight (a sign-out,
    /// a lockout): the session is over. Minting a sibling here would outlive it.</item>
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
        if (await SessionEndedInFlightAsync(lost, cancellationToken))
        {
            _logger.LogInformation(
                "Refresh for user {UserId} lost its rotation to a session end that happened in flight. IP: {IpAddress}",
                lost.UserId, ipAddress);
            return AuthErrors.RefreshTokenRevoked;
        }

        if (answeredFromGrace)
        {
            return await RevokeForReuseAsync(lost.UserId, ipAddress, cancellationToken);
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
    /// A replacement that cannot be found counts as ended: the safe side is to
    /// mint nothing.
    /// </summary>
    private async Task<bool> SessionEndedInFlightAsync(RefreshTokenEntity lost, CancellationToken cancellationToken)
    {
        var current = await _refreshTokenRepository.GetByIdAsync(lost.Id, cancellationToken);
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
    /// A rotated token presented a second time: two parties hold it. Every token
    /// of the user is revoked and the owner is told, once per incident.
    /// </summary>
    private async Task<Error> RevokeForReuseAsync(
        Guid userId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Attempted reuse of revoked refresh token for user {UserId}. Revoking all tokens. IP: {IpAddress}",
            userId, ipAddress);

        var revokedCount = await _refreshTokenRepository.RevokeAllForUserAsync(
            userId,
            null, // revokedBy - system action
            TokenRevocationReasons.RefreshTokenReuse,
            cancellationToken);

        await NotifyReuseDetectedAsync(userId, revokedCount, ipAddress, cancellationToken);

        return AuthErrors.TokenRevoked;
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
