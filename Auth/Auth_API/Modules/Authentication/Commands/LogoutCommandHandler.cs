using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the logout command.
/// </summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ErrorOr<Success>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenBlacklistService tokenBlacklistService,
        IOptions<JwtSettings> jwtSettings,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenBlacklistService = tokenBlacklistService;
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
            // Revoke only the specific token
            var tokenHash = ComputeSha256Hash(request.RefreshToken);
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

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(accessToken))
            {
                return;
            }

            var jwtToken = handler.ReadJwtToken(accessToken);
            var jti = jwtToken.Id;

            if (!string.IsNullOrEmpty(jti))
            {
                // Calculate when this token expires
                var expiresAt = jwtToken.ValidTo;
                if (expiresAt == DateTime.MinValue)
                {
                    // If no expiration in token, use configured lifetime
                    expiresAt = DateTime.UtcNow.Add(_jwtSettings.AccessTokenLifetime);
                }

                _tokenBlacklistService.BlacklistToken(jti, expiresAt);
                _logger.LogDebug("Blacklisted access token with JTI {Jti} for user {UserId}", jti, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse access token for blacklisting");
        }
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
