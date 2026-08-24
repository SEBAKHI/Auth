using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Logout;

/// <summary>
/// Handler for the logout command.
/// </summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ErrorOr<Success>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IJwtTokenService jwtTokenService,
        IUserSessionRepository sessionRepository,
        ICredentialRevocationService credentialRevocation,
        IIdpSessionRepository idpSessionRepository,
        IPublisher publisher,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _jwtTokenService = jwtTokenService;
        _sessionRepository = sessionRepository;
        _credentialRevocation = credentialRevocation;
        _idpSessionRepository = idpSessionRepository;
        _publisher = publisher;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Blacklist the access token to immediately invalidate it
        BlacklistAccessToken(request.AccessToken, request.UserId, request.LogoutAllDevices);

        if (request.LogoutAllDevices)
        {
            // Revoke all tokens for the user
            await _refreshTokenRepository.RevokeAllForUserAsync(
                request.UserId,
                request.UserId, // revokedBy - user initiated
                "User initiated logout from all devices",
                cancellationToken);

            _logger.LogInformation("User {UserId} logged out from all devices", request.UserId);
        }
        else if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            // Compute hash and lookup by hash
            var tokenHash = _refreshTokenKeyService.ComputeTokenHash(request.RefreshToken);
            var token = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

            if (token != null && !token.IsRevoked)
            {
                token.Revoke(request.UserId, "User initiated logout");
                await _refreshTokenRepository.UpdateAsync(token, cancellationToken);

                _logger.LogInformation("User {UserId} logged out from single device", request.UserId);
            }
        }
        else
        {
            // No specific token provided, just acknowledge the logout
            _logger.LogInformation("User {UserId} logout acknowledged (no token to revoke)", request.UserId);
        }

        // End the session row(s). Best-effort: never fail logout.
        try
        {
            if (request.LogoutAllDevices)
            {
                await _sessionRepository.TerminateAllForUserAsync(
                    request.UserId, "logout", cancellationToken);
            }
            else if (request.SessionId.HasValue)
            {
                // Not TerminateAsync. Ending the row stops it showing as active
                // and does nothing else, and the single-device branch above only
                // revokes a refresh token when the client bothered to send one —
                // the console does not. That left "log me out of this device"
                // meaning the access token dies in fifteen minutes and the
                // refresh token keeps working for a week. This ends the row,
                // revokes the refresh tokens bound to that session whether or not
                // the client sent one, and blacklists the sid.
                await _credentialRevocation.TerminateSessionAsync(
                    request.SessionId.Value, request.UserId, "logout", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Above Warning on purpose: the caller has been told it is signed out
            // and its refresh token may still be live, which is the one outcome
            // this path exists to prevent.
            _logger.LogError(ex,
                "Failed to revoke session credentials on logout for user {UserId}; the refresh token may still be usable",
                request.UserId);
        }

        // End the IdP SSO session so future authorize requests require a fresh
        // interactive login. Best-effort: never fail logout.
        try
        {
            if (request.LogoutAllDevices)
            {
                await _idpSessionRepository.RevokeAllForUserAsync(request.UserId, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(request.IdpSessionToken))
            {
                var idpTokenHash = _refreshTokenKeyService.ComputeTokenHash(request.IdpSessionToken);
                var idpSession = await _idpSessionRepository.GetByTokenHashAsync(idpTokenHash, cancellationToken);
                if (idpSession is { IsRevoked: false })
                {
                    idpSession.Revoke();
                    await _idpSessionRepository.UpdateAsync(idpSession, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to revoke IdP session on logout for user {UserId}", request.UserId);
        }

        await _publisher.Publish(
            new UserLoggedOutEvent(request.UserId, request.LogoutAllDevices),
            cancellationToken);

        return Result.Success;
    }

    private void BlacklistAccessToken(string? accessToken, Guid userId, bool logoutAllDevices)
    {
        if (logoutAllDevices)
        {
            // Blacklist all tokens for this user issued before now
            _tokenBlacklistService.BlacklistAllUserTokens(userId, DateTime.UtcNow);
            _logger.LogDebug("Blacklisted all access tokens for user {UserId}", userId);
            return;
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        var jti = _jwtTokenService.GetTokenId(accessToken);
        if (!string.IsNullOrEmpty(jti))
        {
            // Calculate when this token expires
            var expiresAt = _jwtTokenService.GetTokenExpiry(accessToken)
                            ?? DateTime.UtcNow.Add(_jwtSettings.AccessTokenLifetime);

            _tokenBlacklistService.BlacklistToken(jti, expiresAt);
            _logger.LogDebug("Blacklisted access token with JTI {Jti} for user {UserId}", jti, userId);
        }
    }
}
