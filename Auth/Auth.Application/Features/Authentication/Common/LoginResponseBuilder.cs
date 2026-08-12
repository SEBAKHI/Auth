using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
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
    private readonly IUserKnownDeviceRepository _knownDeviceRepository;
    private readonly IGeoIpLookup _geoIpLookup;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly JwtSettings _jwtSettings;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly NotificationSettings _notificationSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly ILogger<LoginResponseBuilder> _logger;

    /// <summary>
    /// Recorded on sessions ended to stay within the concurrent session limit.
    /// Documented alongside the other EndReason writers in UserSessions.sql.
    /// </summary>
    private const string SessionLimitEndReason = "session_limit";

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
        IUserKnownDeviceRepository knownDeviceRepository,
        IGeoIpLookup geoIpLookup,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        IOptionsSnapshot<NotificationSettings> notificationSettings,
        IOptionsSnapshot<SessionSettings> sessionSettings,
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
        _knownDeviceRepository = knownDeviceRepository;
        _geoIpLookup = geoIpLookup;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _jwtSettings = jwtSettings.Value;
        _idpSettings = idpSettings.Value;
        _notificationSettings = notificationSettings.Value;
        _sessionSettings = sessionSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<LoginResponse>> BuildAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken,
        bool establishIdpSession = true,
        string? audience = null,
        Guid? applicationId = null,
        Guid? twoFactorChallengeId = null)
    {
        // The refusal branch of the concurrent session limit, and the first thing
        // this method does. Once past here a refresh token row exists and an
        // access token has been signed; refusing after that would hand out
        // credentials for a sign-in that was rejected.
        var limit = _sessionSettings.MaxConcurrentSessions;
        if (limit > 0 && !_sessionSettings.TerminateOldestOnMax)
        {
            var activeCount = await _sessionRepository.CountActiveForUserAsync(
                user.Id, cancellationToken);

            if (activeCount >= limit)
            {
                // Without this the user opens their own sign-in history and finds
                // nothing where their rejected attempt should be. The reason is
                // written as prose because FailureReason holds text a person
                // reads, not a code. After a two-factor ceremony the refusal
                // settles the row that ceremony already opened — appending here
                // would leave one sign-in showing as both pending and refused.
                await RecordOutcomeAsync(
                    user, ipAddress, userAgent, applicationId, twoFactorChallengeId,
                    succeeded: false,
                    failureReason: "Maximum concurrent sessions reached",
                    cancellationToken);

                _logger.LogInformation(
                    "Refused sign-in for user {UserId}: {ActiveCount} active sessions at a limit of {Limit}",
                    user.Id, activeCount, limit);

                return SessionErrors.MaxSessionsReached;
            }
        }

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

        // Parsed once, here, and handed to both the session row and the device
        // ledger below. Two parses would be two chances to disagree, and the two
        // consumers are the session list the user reads and the security email
        // they receive — "Chrome on Windows" in one and "Edge" in the other reads
        // as a second, unexplained sign-in.
        var parsedAgent = UserAgentParser.Parse(userAgent);
        var deviceName = parsedAgent.Describe();
        var deviceHash = UserKnownDevice.ComputeHash(deviceId, parsedAgent.Browser, parsedAgent.Os);

        // What this browser's signature would have been before the client sent
        // its identifier on every request. Accounts created while verify-email
        // omitted it hold a row under exactly this value, and the ledger would
        // otherwise record the same browser a second time — with a "new device"
        // email announcing it. Null when there is nothing to reconcile: no id
        // was sent, so the current signature already IS the legacy one.
        var legacySignature = string.IsNullOrWhiteSpace(deviceId)
            ? null
            : UserKnownDevice.ComputeHash(null, parsedAgent.Browser, parsedAgent.Os);

        // Generate tokens
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user, permissions, roleNames, sessionId, organizationPermissions, audience);
        var jwtId = _jwtTokenService.GetTokenId(accessToken) ?? Guid.NewGuid().ToString();
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var refreshTokenHash = _refreshTokenKeyService.ComputeTokenHash(refreshToken);

        // Save refresh token (only hash is stored, not plain token). The
        // ApplicationId scopes the token to the requesting app so refreshes
        // re-mint the same per-app audience.
        var refreshTokenEntity = RefreshTokenEntity.Create(
            user.Id,
            refreshTokenHash,
            jwtId,
            applicationId,
            _jwtSettings.RefreshTokenLifetime,
            ipAddress,
            userAgent,
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
                applicationId,                               // which app this sign-in belongs to
                refreshTokenEntity.Id,                       // refreshTokenId
                refreshTokenHash,                            // sessionTokenHash
                ipAddress ?? "unknown",                      // IpAddress is NOT NULL
                userAgent,
                parsedAgent.DeviceType,
                deviceId,
                deviceName,
                deviceHash,
                _geoIpLookup.Resolve(ipAddress),
                now,                                         // createdAt
                now.Add(_jwtSettings.RefreshTokenLifetime),  // expiresAt
                now,                                         // lastActivityAt
                true,                                        // isActive
                null,                                        // terminatedAt
                null);                                       // terminationReason
            await _sessionRepository.CreateAsync(session, cancellationToken);

            await TrackDeviceAsync(
                user, ipAddress, deviceHash, legacySignature, deviceName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create session record for user {UserId}", user.Id);
        }

        // The eviction branch of the limit. A separate try/catch from the block
        // above, in both directions: a session row that failed to insert must not
        // skip enforcement (the account can already be over the limit from
        // earlier sign-ins), and a failed eviction must not discard a session that
        // was written successfully. Running after the insert is what guarantees
        // the sign-in happening right now is the most recently used session and
        // therefore always survives.
        //
        // Failure here is logged and swallowed like the rest of session tracking:
        // the user is authenticated, and TerminateBeyondLimitAsync ends everything
        // past the limit rather than one row, so the next sign-in corrects it.
        if (limit > 0 && _sessionSettings.TerminateOldestOnMax)
        {
            try
            {
                var evicted = await _credentialRevocation.EnforceConcurrentSessionLimitAsync(
                    user.Id, limit, SessionLimitEndReason, cancellationToken);

                if (evicted.Count > 0)
                {
                    await _publisher.Publish(
                        new SessionLimitEnforcedEvent(
                            user.Id,
                            user.Email,
                            user.DisplayName ?? user.FirstName,
                            [.. evicted.Select(s => new EndedSession(
                                s.Id, s.DeviceName, s.IpAddress, s.LastActivityAt))],
                            deviceName,
                            limit,
                            DateTime.UtcNow),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to enforce the concurrent session limit for user {UserId}", user.Id);
            }
        }

        // Record successful login
        await _userRepository.RecordSuccessfulLoginAsync(user.Id, ipAddress, cancellationToken);

        // The agent is recorded so the user's own sign-in history can name the
        // client. It used to be dropped here, which left every successful entry
        // in that history describing nothing.
        await RecordOutcomeAsync(
            user, ipAddress, userAgent, applicationId, twoFactorChallengeId,
            succeeded: true,
            failureReason: null,
            cancellationToken);

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
                    userAgent);
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

    /// <summary>
    /// Records the device this sign-in came from and, when it is one the user
    /// has not been seen on before, raises the event that tells them so.
    ///
    /// Takes the signature and label already computed for the session row rather
    /// than deriving its own: the ledger's key must be the same key the session
    /// carries, or the two can never be joined.
    ///
    /// Called from inside the session-tracking try/catch on purpose: every
    /// successful sign-in passes through here — password, external provider,
    /// two-factor completion, email verification, account recovery — and none
    /// of them may fail because a notification could not be arranged.
    /// </summary>
    /// <summary>
    /// Writes the sign-in's outcome as exactly one row, whichever way it ended.
    ///
    /// A sign-in that went through a second factor already has a row — opened by
    /// the challenge service in the earlier request — so its outcome settles that
    /// row. Everything else has no row yet and inserts one. Both callers here go
    /// through this method so the two branches cannot drift into disagreeing
    /// about how many rows a single sign-in produces.
    /// </summary>
    private async Task RecordOutcomeAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        Guid? applicationId,
        Guid? twoFactorChallengeId,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        if (twoFactorChallengeId.HasValue)
        {
            await _loginAttemptRepository.ResolveTwoFactorCeremonyAsync(
                twoFactorChallengeId.Value, succeeded, failureReason, cancellationToken);
            return;
        }

        var attempt = succeeded
            ? LoginAttempt.CreateSuccess(user.Id, user.Email, ipAddress, userAgent, applicationId: applicationId)
            : LoginAttempt.CreateFailure(
                user.Email, failureReason!, ipAddress, userAgent, user.Id, applicationId: applicationId);

        await _loginAttemptRepository.CreateAsync(attempt, cancellationToken);
    }

    private async Task TrackDeviceAsync(
        User user,
        string? ipAddress,
        string deviceHash,
        string? legacySignature,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        if (!_notificationSettings.NewDeviceAlertEnabled)
        {
            return;
        }

        var known = await _knownDeviceRepository.GetAsync(user.Id, deviceHash, cancellationToken);
        if (known is not null)
        {
            known.Touch(deviceName);
            await _knownDeviceRepository.UpsertAsync(known, cancellationToken);
            return;
        }

        // Before treating this as a browser we have never seen, check whether it
        // is one we recorded under its pre-header signature. Only reached on a
        // miss, so a recognised browser still costs a single lookup.
        if (legacySignature is not null
            && await _knownDeviceRepository.AdoptLegacySignatureAsync(
                user.Id, legacySignature, deviceHash, cancellationToken))
        {
            var adopted = await _knownDeviceRepository.GetAsync(
                user.Id, deviceHash, cancellationToken);

            if (adopted is not null)
            {
                adopted.Touch(deviceName);
                await _knownDeviceRepository.UpsertAsync(adopted, cancellationToken);
            }

            // Deliberately no alert: the same browser under a better name is not
            // a new device, and saying so would be the false alarm this whole
            // reconciliation exists to prevent.
            return;
        }

        // The user's very first device must not raise an alert: it would be
        // reporting the sign-in they are performing right now, moments after
        // registering, which reads as a compromise rather than a welcome.
        var isFirstEver = !await _knownDeviceRepository.HasAnyAsync(user.Id, cancellationToken);

        // Someone who clears site data every session presents a new signature
        // every time; without a per-user floor that is one email per sign-in.
        var lastAlertAt = isFirstEver
            ? null
            : await _knownDeviceRepository.GetLastAlertAtAsync(user.Id, cancellationToken);
        var throttled = lastAlertAt.HasValue
            && DateTime.UtcNow - lastAlertAt.Value < _notificationSettings.NewDeviceAlertMinInterval;

        var alerting = !isFirstEver && !throttled;

        // The insert decides: two concurrent sign-ins from the same new device
        // both reach here, and only the one that actually created the row is
        // the discovery.
        var inserted = await _knownDeviceRepository.UpsertAsync(
            UserKnownDevice.Create(
                user.Id,
                deviceHash,
                deviceName,
                alerting ? DateTime.UtcNow : null),
            cancellationToken);

        if (!alerting || !inserted)
        {
            return;
        }

        await _publisher.Publish(
            new NewDeviceSignInEvent(
                user.Id,
                user.Email,
                user.DisplayName ?? user.FirstName,
                deviceName,
                ipAddress,
                DateTime.UtcNow),
            cancellationToken);
    }
}
