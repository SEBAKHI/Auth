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
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenBlacklistService tokenBlacklistService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IJwtTokenService jwtTokenService,
        IPublisher publisher,
        IOptions<JwtSettings> jwtSettings,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenBlacklistService = tokenBlacklistService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _jwtTokenService = jwtTokenService;
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
