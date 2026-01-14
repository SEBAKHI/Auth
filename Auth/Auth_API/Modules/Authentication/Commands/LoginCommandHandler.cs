using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Enums;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the login command.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, ErrorOr<LoginResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly PasswordSettings _passwordSettings;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IOptions<PasswordSettings> passwordSettings,
        IOptions<JwtSettings> jwtSettings,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _passwordSettings = passwordSettings.Value;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Get user by email
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null)
        {
            // Record failed attempt even if user doesn't exist (prevent enumeration)
            await RecordLoginAttemptAsync(null, request.Email, false, "User not found",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Check account status
        var statusCheck = CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            await RecordLoginAttemptAsync(user.Id, request.Email, false, statusCheck.FirstError.Description,
                request.IpAddress, request.UserAgent, cancellationToken);

            return statusCheck.Errors;
        }

        // Check lockout
        if (user.IsLockedOut())
        {
            await RecordLoginAttemptAsync(user.Id, request.Email, false, "Account locked",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        // Verify password
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            // Record failed attempt and potentially lock account
            await _userRepository.RecordFailedLoginAsync(
                user.Id,
                _passwordSettings.MaxFailedAttempts,
                _passwordSettings.LockoutDuration,
                cancellationToken);

            await RecordLoginAttemptAsync(user.Id, request.Email, false, "Invalid password",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Failed login attempt for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return UserErrors.InvalidCredentials;
        }

        // Check if email is confirmed (only after successful password verification)
        if (!user.EmailConfirmed)
        {
            await RecordLoginAttemptAsync(user.Id, request.Email, false, "Email not confirmed",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Login blocked for user {UserId} - email not confirmed", user.Id);

            return UserErrors.EmailNotConfirmed;
        }

        // Check if password needs rehash (parameters changed)
        if (_passwordHasher.NeedsRehash(user.PasswordHash))
        {
            var newHash = _passwordHasher.HashPassword(request.Password);
            await _userRepository.UpdatePasswordAsync(user.Id, newHash, user.Id, cancellationToken);
            _logger.LogInformation("Rehashed password for user {UserId} due to parameter changes", user.Id);
        }

        // Get roles and permissions
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roleNames = roles.Select(r => r.Code).ToList();
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        // Generate tokens
        var accessToken = _jwtTokenService.GenerateAccessToken(user, permissions, roleNames);
        var jwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = _refreshTokenKeyService.ComputeTokenHash(refreshToken);

        // Build device info from user agent and device ID
        var deviceInfo = BuildDeviceInfo(request.UserAgent, request.DeviceId);

        // Save refresh token (only hash is stored, not plain token)
        var refreshTokenEntity = RefreshToken.Create(
            user.Id,
            refreshTokenHash,
            jwtId,
            null, // ApplicationId - can be set if request includes it
            _jwtSettings.RefreshTokenLifetime,
            request.IpAddress,
            deviceInfo);

        await _refreshTokenRepository.CreateAsync(refreshTokenEntity, cancellationToken);

        // Record successful login
        await _userRepository.RecordSuccessfulLoginAsync(user.Id, cancellationToken);
        await RecordLoginAttemptAsync(user.Id, request.Email, true, null,
            request.IpAddress, request.UserAgent, cancellationToken);

        _logger.LogInformation("User {UserId} logged in successfully from {IpAddress}",
            user.Id, request.IpAddress);

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

    private static ErrorOr<Success> CheckAccountStatus(User user)
    {
        return user.Status switch
        {
            UserStatus.Inactive => UserErrors.AccountInactive,
            UserStatus.Locked => UserErrors.AccountLocked,
            UserStatus.Pending => UserErrors.AccountPending,
            _ => Result.Success
        };
    }

    private static string? BuildDeviceInfo(string? userAgent, string? deviceId)
    {
        if (string.IsNullOrEmpty(userAgent) && string.IsNullOrEmpty(deviceId))
            return null;

        if (string.IsNullOrEmpty(deviceId))
            return userAgent;

        if (string.IsNullOrEmpty(userAgent))
            return $"DeviceId: {deviceId}";

        return $"{userAgent} | DeviceId: {deviceId}";
    }

    private async Task RecordLoginAttemptAsync(
        Guid? userId,
        string email,
        bool isSuccess,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var attempt = isSuccess
            ? LoginAttempt.CreateSuccess(userId!.Value, email, ipAddress, userAgent)
            : LoginAttempt.CreateFailure(email, failureReason!, ipAddress, userAgent, userId);

        await _loginAttemptRepository.CreateAsync(attempt, cancellationToken);
    }
}
