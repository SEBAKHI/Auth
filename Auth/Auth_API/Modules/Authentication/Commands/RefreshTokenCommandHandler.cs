using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the refresh token command.
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, ErrorOr<TokenResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        IOptions<JwtSettings> jwtSettings,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<TokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Hash the incoming token using Argon2id to find it in the database
        var tokenHash = _passwordHasher.HashPassword(request.RefreshToken);

        // Find the refresh token
        var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (storedToken == null)
        {
            _logger.LogWarning("Refresh token not found. IP: {IpAddress}", request.IpAddress);
            return AuthErrors.RefreshTokenNotFound;
        }

        // Check if token is revoked
        if (storedToken.IsRevoked)
        {
            // Possible token reuse attack - revoke all tokens for this user
            _logger.LogWarning(
                "Attempted reuse of revoked refresh token for user {UserId}. Revoking all tokens. IP: {IpAddress}",
                storedToken.UserId, request.IpAddress);

            await _refreshTokenRepository.RevokeAllForUserAsync(
                storedToken.UserId,
                null, // revokedBy - system action
                "Detected refresh token reuse",
                cancellationToken);

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

        // Check user is still active
        if (user.IsLockedOut())
        {
            await _refreshTokenRepository.RevokeAllForUserAsync(
                user.Id,
                null, // revokedBy - system action
                "User account locked",
                cancellationToken);

            return UserErrors.AccountLocked;
        }

        // Get updated roles and permissions
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roleNames = roles.Select(r => r.Code).ToList();
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        // Generate new access token
        var accessToken = _jwtTokenService.GenerateAccessToken(user, permissions, roleNames);

        string newRefreshToken;
        int refreshExpiresIn;

        // Rotate refresh token if enabled
        if (_jwtSettings.RotateRefreshTokens)
        {
            var (newToken, newTokenHash) = _jwtTokenService.GenerateRefreshToken();
            var newJwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
            newRefreshToken = newToken;

            // Create new token
            var newRefreshTokenEntity = RefreshToken.Create(
                user.Id,
                newToken,
                newTokenHash,
                newJwtId,
                storedToken.ApplicationId,
                _jwtSettings.RefreshTokenLifetime,
                request.IpAddress,
                storedToken.DeviceInfo);

            await _refreshTokenRepository.CreateAsync(newRefreshTokenEntity, cancellationToken);

            // Revoke old token (pass the new token string, not the entity ID)
            storedToken.Revoke(user.Id, "Rotated", newToken);
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
}
