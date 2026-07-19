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
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<LoginResponseBuilder> _logger;

    public LoginResponseBuilder(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IOrganizationRepository organizationRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IUserSessionRepository sessionRepository,
        IIdpSessionRepository idpSessionRepository,
        IOptions<JwtSettings> jwtSettings,
        IOptions<IdentityProviderSettings> idpSettings,
        ILogger<LoginResponseBuilder> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _organizationRepository = organizationRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _sessionRepository = sessionRepository;
        _idpSessionRepository = idpSessionRepository;
        _jwtSettings = jwtSettings.Value;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResponse> BuildAsync(
        User user,
        string? ipAddress,
        string? deviceInfo,
        CancellationToken cancellationToken,
        bool establishIdpSession = true)
    {
        // Get roles and permissions
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roleNames = roles.Select(r => r.Code).ToList();
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        // Organization membership permissions ride in the token as org-scoped
        // claims so members pass the org endpoint gates for their own orgs.
        var organizationPermissions = await _organizationRepository
            .GetMembershipPermissionCodesAsync(user.Id, cancellationToken);

        // A stable session id, constant across access-token refreshes, ties the
        // session row and all of its refresh tokens together (carried as "sid").
        var sessionId = Guid.NewGuid();

        // Generate tokens
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user, permissions, roleNames, sessionId, organizationPermissions);
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
            deviceInfo,
            sessionId);

        await _refreshTokenRepository.CreateAsync(refreshTokenEntity, cancellationToken);

        // Persist a session row so the login appears under the user's active
        // sessions. Its Id equals the access token's "sid" claim so it stays the
        // current session across refreshes. Session tracking must never break the
        // login flow, so failures are logged and swallowed.
        try
        {
            var now = DateTime.UtcNow;
            var session = new UserSession(
                sessionId,
                user.Id,
                null,                                        // applicationId — not app-scoped
                refreshTokenEntity.Id,                       // refreshTokenId
                refreshTokenHash,                            // sessionTokenHash
                ipAddress ?? "unknown",                      // IpAddress is NOT NULL
                deviceInfo,                                  // userAgent
                null,                                        // deviceId
                null,                                        // deviceName
                null,                                        // location
                now,                                         // createdAt
                now.Add(_jwtSettings.RefreshTokenLifetime),  // expiresAt
                now,                                         // lastActivityAt
                true,                                        // isActive
                null,                                        // terminatedAt
                null);                                       // terminationReason
            await _sessionRepository.CreateAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create session record for user {UserId}", user.Id);
        }

        // Record successful login
        await _userRepository.RecordSuccessfulLoginAsync(user.Id, ipAddress, cancellationToken);

        var loginAttempt = LoginAttempt.CreateSuccess(user.Id, user.Email, ipAddress, null);
        await _loginAttemptRepository.CreateAsync(loginAttempt, cancellationToken);

        _logger.LogInformation("User {UserId} logged in successfully from {IpAddress}",
            user.Id, ipAddress);

        // Mint the IdP SSO session (the cookie's server-side counterpart) so a
        // later authorize request can recognize the browser. Like session-row
        // tracking above, this must never break the login itself.
        string? idpSessionToken = null;
        if (establishIdpSession)
        {
            try
            {
                var plainIdpToken = _jwtTokenService.GenerateRefreshToken();
                var idpSession = IdpSession.Create(
                    user.Id,
                    _refreshTokenKeyService.ComputeTokenHash(plainIdpToken),
                    _idpSettings.IdpSessionLifetime,
                    ipAddress,
                    deviceInfo);
                await _idpSessionRepository.CreateAsync(idpSession, cancellationToken);
                idpSessionToken = plainIdpToken;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to create IdP session for user {UserId}", user.Id);
            }
        }

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
            Theme = user.Theme,
            Roles = roleNames,
            Permissions = permissions.ToList()
        };

        return new LoginResponse
        {
            Token = tokenResponse,
            User = userInfo,
            RequiresPasswordChange = user.MustChangePassword,
            // Tokens are only issued once 2FA is satisfied (or not enabled),
            // so a built response never requires further verification.
            RequiresTwoFactor = false,
            IdpSessionToken = idpSessionToken
        };
    }
}
