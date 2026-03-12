using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared service that builds a LoginResponse with JWT tokens and user info.
/// Used by both LoginCommandHandler and ExternalLoginCommandHandler to avoid
/// duplicating the token generation + response building logic.
/// </summary>
public class LoginResponseBuilder : ILoginResponseBuilder
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LoginResponseBuilder> _logger;

    public LoginResponseBuilder(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IOptions<JwtSettings> jwtSettings,
        ILogger<LoginResponseBuilder> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResponse> BuildAsync(
        User user,
        string? ipAddress,
        string? deviceInfo,
        CancellationToken cancellationToken = default)
    {
        // Get roles and permissions
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roleNames = roles.Select(r => r.Code).ToList();
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        // Generate tokens
        var accessToken = _jwtTokenService.GenerateAccessToken(user, permissions, roleNames);
        var jwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = _refreshTokenKeyService.ComputeTokenHash(refreshToken);

        // Save refresh token (only hash is stored, not plain token)
        var refreshTokenEntity = RefreshTokenEntity.Create(
            user.Id,
            refreshTokenHash,
            jwtId,
            null,
            _jwtSettings.RefreshTokenLifetime,
            ipAddress,
            deviceInfo);

        await _refreshTokenRepository.CreateAsync(refreshTokenEntity, cancellationToken);

        // Record successful login
        await _userRepository.RecordSuccessfulLoginAsync(user.Id, cancellationToken);

        var loginAttempt = LoginAttempt.CreateSuccess(user.Id, user.Email, ipAddress, null);
        await _loginAttemptRepository.CreateAsync(loginAttempt, cancellationToken);

        _logger.LogInformation("User {UserId} logged in successfully from {IpAddress}",
            user.Id, ipAddress);

        // Build response
        var tokenResponse = new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = (int)_jwtSettings.AccessTokenLifetime.TotalSeconds,
            RefreshExpiresIn = (int)_jwtSettings.RefreshTokenLifetime.TotalSeconds
        };

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PreferredLanguage = user.PreferredLanguage,
            TimeZone = user.TimeZone,
            Roles = roleNames,
            Permissions = permissions.ToList()
        };

        return new LoginResponse
        {
            Token = tokenResponse,
            User = userInfo,
            RequiresPasswordChange = user.MustChangePassword,
            RequiresTwoFactor = user.TwoFactorEnabled
        };
    }
}
