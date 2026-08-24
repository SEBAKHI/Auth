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
        var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (storedToken == null)
        {
            _logger.LogWarning("Refresh token not found. IP: {IpAddress}", request.IpAddress);
            return AuthErrors.RefreshTokenNotFound;
        }

        // Check if token is revoked
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

            // Possible token reuse attack - revoke all tokens for this user
            _logger.LogWarning(
                "Attempted reuse of revoked refresh token for user {UserId}. Revoking all tokens. IP: {IpAddress}",
                storedToken.UserId, request.IpAddress);

            var revokedCount = await _refreshTokenRepository.RevokeAllForUserAsync(
                storedToken.UserId,
                null, // revokedBy - system action
                TokenRevocationReasons.RefreshTokenReuse,
                cancellationToken);

            await NotifyReuseDetectedAsync(
                storedToken.UserId, revokedCount, request.IpAddress, cancellationToken);

            return AuthErrors.TokenRevoked;
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

            await _refreshTokenRepository.CreateAsync(newRefreshTokenEntity, cancellationToken);

            // Revoke old token (pass the new token hash for tracking, not plain token)
            storedToken.Revoke(user.Id, TokenRevocationReasons.Rotated, newTokenHash);
            await _refreshTokenRepository.UpdateAsync(storedToken, cancellationToken);

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
