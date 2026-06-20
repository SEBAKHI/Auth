using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using ErrorOr;
using Microsoft.Extensions.Options;
using Moq;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Factory methods for creating test entities.
/// These methods properly construct entities using their full constructors
/// since domain entities have immutable properties with private setters.
/// </summary>
public static class TestHelpers
{
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Creates a test User entity.
    /// </summary>
    public static User CreateUser(
        Guid? id = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null,
        UserStatus status = UserStatus.Active,
        bool emailConfirmed = true,
        Guid? createdBy = null)
    {
        var userId = id ?? Guid.NewGuid();
        var userEmail = email ?? $"user-{userId:N}@test.com";

        return new User(
            id: userId,
            email: userEmail,
            normalizedEmail: userEmail.ToUpperInvariant(),
            passwordHash: "TestPasswordHash",
            firstName: firstName ?? "Test",
            lastName: lastName ?? "User",
            displayName: null,
            phoneNumber: null,
            status: status,
            emailConfirmed: emailConfirmed,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 0,
            lockoutEnd: null,
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test Organization entity.
    /// </summary>
    public static Organization CreateOrganization(
        Guid? id = null,
        string? code = null,
        string? name = null,
        string? description = null,
        string? contactEmail = null,
        Guid? ownerId = null,
        bool isActive = true,
        Guid? createdBy = null)
    {
        var orgId = id ?? Guid.NewGuid();
        var owner = ownerId ?? Guid.NewGuid();

        return new Organization(
            id: orgId,
            code: code ?? $"org-{orgId:N}"[..20],
            name: name ?? "Test Organization",
            description: description,
            logoUrl: null,
            website: null,
            contactEmail: contactEmail ?? $"contact@{orgId:N}.test.com",
            ownerId: owner,
            isActive: isActive,
            isAutoCreated: false,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? owner,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test Role entity.
    /// </summary>
    public static Role CreateRole(
        Guid? id = null,
        Guid? applicationId = null,
        string? code = null,
        string? name = null,
        string? description = null,
        bool isActive = true,
        bool isSystem = false,
        Guid? createdBy = null)
    {
        var roleId = id ?? Guid.NewGuid();

        return new Role(
            id: roleId,
            applicationId: applicationId,
            code: code ?? $"ROLE-{roleId:N}"[..20].ToUpperInvariant(),
            name: name ?? "Test Role",
            description: description,
            isActive: isActive,
            isSystem: isSystem,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test Application entity.
    /// </summary>
    public static Application CreateApplication(
        Guid? id = null,
        string? code = null,
        string? name = null,
        string? description = null,
        string? baseUrl = null,
        string? logoUrl = null,
        bool isActive = true,
        Guid? createdBy = null)
    {
        var appId = id ?? Guid.NewGuid();

        return new Application(
            id: appId,
            code: code ?? $"APP-{appId:N}"[..15].ToUpperInvariant(),
            name: name ?? "Test Application",
            description: description,
            baseUrl: baseUrl ?? "https://test.example.com",
            logoUrl: logoUrl,
            contactEmail: null,
            isActive: isActive,
            allowSelfRegistration: false,
            requireTwoFactor: false,
            requireEmailVerification: false,
            sessionTimeoutMinutes: 60,
            maxConcurrentSessions: 5,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test OrganizationUser (membership) entity.
    /// </summary>
    public static OrganizationUser CreateOrganizationUser(
        Guid? id = null,
        Guid? organizationId = null,
        Guid? userId = null,
        Guid? roleId = null,
        bool isActive = true,
        DateTime? joinedAt = null,
        Guid? invitedBy = null,
        DateTime? expiresAt = null,
        Guid? createdBy = null)
    {
        var membershipId = id ?? Guid.NewGuid();
        var inviter = invitedBy ?? Guid.NewGuid();

        return new OrganizationUser(
            id: membershipId,
            organizationId: organizationId ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            roleId: roleId ?? Guid.NewGuid(),
            isActive: isActive,
            joinedAt: joinedAt ?? DateTime.UtcNow,
            invitedBy: inviter,
            expiresAt: expiresAt,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? inviter,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test OrganizationApplication (subscription) entity.
    /// </summary>
    public static OrganizationApplication CreateOrganizationApplication(
        Guid? id = null,
        Guid? organizationId = null,
        Guid? applicationId = null,
        bool isActive = true,
        DateTime? enabledAt = null,
        Guid? enabledBy = null,
        DateTime? expiresAt = null,
        string? subscriptionTier = null,
        Guid? createdBy = null)
    {
        var subscriptionId = id ?? Guid.NewGuid();
        var enabler = enabledBy ?? Guid.NewGuid();

        return new OrganizationApplication(
            id: subscriptionId,
            organizationId: organizationId ?? Guid.NewGuid(),
            applicationId: applicationId ?? Guid.NewGuid(),
            isActive: isActive,
            enabledAt: enabledAt ?? DateTime.UtcNow,
            enabledBy: enabler,
            expiresAt: expiresAt,
            subscriptionTier: subscriptionTier,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? enabler,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test OrganizationInvitation entity.
    /// </summary>
    public static OrganizationInvitation CreateOrganizationInvitation(
        Guid? id = null,
        Guid? organizationId = null,
        string? email = null,
        Guid? roleId = null,
        string? token = null,
        InvitationStatus status = InvitationStatus.Pending,
        DateTime? expiresAt = null,
        Guid? invitedBy = null,
        DateTime? acceptedAt = null,
        Guid? acceptedByUserId = null,
        DateTime? createdAt = null)
    {
        var invitationId = id ?? Guid.NewGuid();

        return new OrganizationInvitation(
            id: invitationId,
            organizationId: organizationId ?? Guid.NewGuid(),
            email: email ?? $"invited-{invitationId:N}@test.com",
            roleId: roleId ?? Guid.NewGuid(),
            token: token ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            status: status,
            expiresAt: expiresAt ?? DateTime.UtcNow.AddDays(7),
            invitedBy: invitedBy ?? Guid.NewGuid(),
            acceptedAt: acceptedAt,
            acceptedByUserId: acceptedByUserId,
            createdAt: createdAt ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a test Permission entity.
    /// </summary>
    public static Permission CreatePermission(
        Guid? id = null,
        Guid? applicationId = null,
        string? code = null,
        string? name = null,
        string? description = null,
        Guid? parentId = null,
        byte level = 3,
        bool isWildcard = false,
        bool isActive = true,
        Guid? createdBy = null)
    {
        var permissionId = id ?? Guid.NewGuid();

        return new Permission(
            id: permissionId,
            applicationId: applicationId,
            code: code ?? $"test:permission:{permissionId:N}"[..30],
            name: name ?? "Test Permission",
            description: description,
            parentId: parentId,
            level: level,
            isWildcard: isWildcard,
            isActive: isActive,
            createdAt: DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            modifiedAt: null,
            modifiedBy: null);
    }

    /// <summary>
    /// Creates a test RefreshToken entity.
    /// </summary>
    public static RefreshToken CreateRefreshToken(
        Guid? id = null,
        Guid? userId = null,
        string? tokenHash = null,
        string? jwtId = null,
        Guid? applicationId = null,
        string? deviceInfo = null,
        string? ipAddress = null,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        Guid? revokedBy = null,
        string? replacedByTokenHash = null,
        string? reasonRevoked = null)
    {
        return new RefreshToken(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            tokenHash: tokenHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            jwtId: jwtId ?? Guid.NewGuid().ToString(),
            applicationId: applicationId,
            deviceInfo: deviceInfo,
            ipAddress: ipAddress ?? "127.0.0.1",
            createdAt: createdAt ?? DateTime.UtcNow,
            expiresAt: expiresAt ?? DateTime.UtcNow.AddDays(7),
            revokedAt: revokedAt,
            revokedBy: revokedBy,
            replacedByTokenHash: replacedByTokenHash,
            reasonRevoked: reasonRevoked);
    }

    /// <summary>
    /// Creates a test UserSession entity.
    /// </summary>
    public static UserSession CreateUserSession(
        Guid? id = null,
        Guid? userId = null,
        Guid? applicationId = null,
        Guid? refreshTokenId = null,
        string? sessionTokenHash = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? deviceId = null,
        string? deviceName = null,
        string? location = null,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        DateTime? lastActivityAt = null,
        bool isActive = true,
        DateTime? terminatedAt = null,
        string? terminationReason = null)
    {
        var now = DateTime.UtcNow;
        return new UserSession(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            applicationId: applicationId ?? Guid.NewGuid(),
            refreshTokenId: refreshTokenId,
            sessionTokenHash: sessionTokenHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            ipAddress: ipAddress ?? "127.0.0.1",
            userAgent: userAgent ?? "TestAgent/1.0",
            deviceId: deviceId,
            deviceName: deviceName,
            location: location,
            createdAt: createdAt ?? now,
            expiresAt: expiresAt ?? now.AddHours(24),
            lastActivityAt: lastActivityAt ?? now,
            isActive: isActive,
            terminatedAt: terminatedAt,
            terminationReason: terminationReason);
    }

    /// <summary>
    /// Creates a test LoginAttempt entity.
    /// </summary>
    public static LoginAttempt CreateLoginAttempt(
        Guid? id = null,
        Guid? userId = null,
        string? email = null,
        bool isSuccess = true,
        string? failureReason = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? location = null,
        DateTime? attemptedAt = null,
        Guid? applicationId = null)
    {
        return new LoginAttempt(
            id: id ?? Guid.NewGuid(),
            userId: userId,
            email: email ?? "test@example.com",
            isSuccess: isSuccess,
            failureReason: failureReason,
            ipAddress: ipAddress ?? "127.0.0.1",
            userAgent: userAgent ?? "TestAgent/1.0",
            location: location,
            attemptedAt: attemptedAt ?? DateTime.UtcNow,
            applicationId: applicationId);
    }

    /// <summary>
    /// Creates a test PasswordHistory entity.
    /// </summary>
    public static PasswordHistory CreatePasswordHistory(
        Guid? id = null,
        Guid? userId = null,
        string? passwordHash = null,
        DateTime? createdAt = null)
    {
        return new PasswordHistory(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            passwordHash: passwordHash ?? $"OldHash_{Guid.NewGuid():N}",
            createdAt: createdAt ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a test PasswordResetToken entity.
    /// </summary>
    public static PasswordResetToken CreatePasswordResetToken(
        Guid? id = null,
        Guid? userId = null,
        string? tokenHash = null,
        DateTime? expiresAt = null,
        DateTime? usedAt = null,
        DateTime? createdAt = null)
    {
        return new PasswordResetToken(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            tokenHash: tokenHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            expiresAt: expiresAt ?? DateTime.UtcNow.AddHours(1),
            usedAt: usedAt,
            createdAt: createdAt ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a test EmailVerificationToken entity.
    /// </summary>
    public static EmailVerificationToken CreateEmailVerificationToken(
        Guid? id = null,
        Guid? userId = null,
        string? otpHash = null,
        string? email = null,
        DateTime? expiresAt = null,
        DateTime? usedAt = null,
        int attemptCount = 0,
        DateTime? createdAt = null)
    {
        return new EmailVerificationToken(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            otpHash: otpHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            email: email ?? "test@example.com",
            expiresAt: expiresAt ?? DateTime.UtcNow.AddMinutes(15),
            usedAt: usedAt,
            attemptCount: attemptCount,
            createdAt: createdAt ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a test TwoFactorAuth entity.
    /// </summary>
    public static TwoFactorAuth CreateTwoFactorAuth(
        Guid? id = null,
        Guid? userId = null,
        string? secretKey = null,
        string? recoveryCodes = null,
        bool isEnabled = false,
        DateTime? enabledAt = null,
        DateTime? lastUsedAt = null,
        int failedAttempts = 0,
        DateTime? lockedUntil = null,
        DateTime? createdAt = null,
        DateTime? modifiedAt = null)
    {
        return new TwoFactorAuth(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            secretKey: secretKey ?? "TESTBASE32SECRET",
            recoveryCodes: recoveryCodes,
            isEnabled: isEnabled,
            enabledAt: enabledAt,
            lastUsedAt: lastUsedAt,
            failedAttempts: failedAttempts,
            lockedUntil: lockedUntil,
            createdAt: createdAt ?? DateTime.UtcNow,
            modifiedAt: modifiedAt);
    }

    /// <summary>
    /// Creates a test ApiKey entity.
    /// </summary>
    public static ApiKey CreateApiKey(
        Guid? id = null,
        Guid? applicationId = null,
        string? name = null,
        string? description = null,
        string? keyPrefix = null,
        string? keyHash = null,
        string environment = "production",
        int rateLimitPerMinute = 60,
        int rateLimitPerDay = 10000,
        string? allowedIps = null,
        string? allowedOrigins = null,
        DateTime? createdAt = null,
        Guid? createdBy = null,
        DateTime? expiresAt = null,
        DateTime? lastUsedAt = null,
        DateTime? revokedAt = null,
        Guid? revokedBy = null,
        string? revokeReason = null)
    {
        return new ApiKey(
            id: id ?? Guid.NewGuid(),
            applicationId: applicationId ?? Guid.NewGuid(),
            name: name ?? "Test API Key",
            description: description,
            keyPrefix: keyPrefix ?? "ak_test_",
            keyHash: keyHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            environment: environment,
            rateLimitPerMinute: rateLimitPerMinute,
            rateLimitPerDay: rateLimitPerDay,
            allowedIps: allowedIps,
            allowedOrigins: allowedOrigins,
            createdAt: createdAt ?? DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            expiresAt: expiresAt,
            lastUsedAt: lastUsedAt,
            revokedAt: revokedAt,
            revokedBy: revokedBy,
            revokeReason: revokeReason);
    }

    /// <summary>
    /// Creates a test ApiKeyScope entity.
    /// </summary>
    public static ApiKeyScope CreateApiKeyScope(
        Guid? id = null,
        Guid? apiKeyId = null,
        Guid? permissionId = null,
        DateTime? grantedAt = null,
        Guid? grantedBy = null)
    {
        return new ApiKeyScope(
            id: id ?? Guid.NewGuid(),
            apiKeyId: apiKeyId ?? Guid.NewGuid(),
            permissionId: permissionId ?? Guid.NewGuid(),
            grantedAt: grantedAt ?? DateTime.UtcNow,
            grantedBy: grantedBy ?? SystemUserId);
    }

    /// <summary>
    /// Creates a test WebhookKey entity.
    /// </summary>
    public static WebhookKey CreateWebhookKey(
        Guid? id = null,
        Guid? applicationId = null,
        string? name = null,
        string? description = null,
        string? keyPrefix = null,
        string? keyHash = null,
        string? targetUrl = null,
        string environment = "production",
        DateTime? createdAt = null,
        Guid? createdBy = null,
        DateTime? expiresAt = null,
        DateTime? lastUsedAt = null,
        DateTime? revokedAt = null,
        Guid? revokedBy = null,
        string? revokeReason = null)
    {
        return new WebhookKey(
            id: id ?? Guid.NewGuid(),
            applicationId: applicationId ?? Guid.NewGuid(),
            name: name ?? "Test Webhook Key",
            description: description,
            keyPrefix: keyPrefix ?? "wk_test_",
            keyHash: keyHash ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            targetUrl: targetUrl ?? "https://webhook.test.com/callback",
            environment: environment,
            createdAt: createdAt ?? DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId,
            expiresAt: expiresAt,
            lastUsedAt: lastUsedAt,
            revokedAt: revokedAt,
            revokedBy: revokedBy,
            revokeReason: revokeReason);
    }

    /// <summary>
    /// Creates a test AuditLog entity.
    /// </summary>
    public static AuditLog CreateAuditLog(
        Guid? id = null,
        Guid? userId = null,
        Guid? applicationId = null,
        string? actionType = null,
        string? action = null,
        string? entityType = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? additionalData = null,
        bool isSuccess = true,
        string? errorMessage = null,
        DateTime? timestamp = null,
        string? correlationId = null)
    {
        return new AuditLog(
            id: id ?? Guid.NewGuid(),
            userId: userId,
            applicationId: applicationId,
            actionType: actionType ?? "TestAction",
            action: action ?? "test.action",
            entityType: entityType,
            entityId: entityId,
            oldValues: oldValues,
            newValues: newValues,
            ipAddress: ipAddress ?? "127.0.0.1",
            userAgent: userAgent,
            additionalData: additionalData,
            isSuccess: isSuccess,
            errorMessage: errorMessage,
            timestamp: timestamp ?? DateTime.UtcNow,
            correlationId: correlationId);
    }

    /// <summary>
    /// Creates a test UserRole entity.
    /// </summary>
    public static UserRole CreateUserRole(
        Guid? id = null,
        Guid? userId = null,
        Guid? roleId = null,
        Guid? applicationId = null,
        DateTime? assignedAt = null,
        Guid? assignedBy = null,
        DateTime? expiresAt = null,
        bool isActive = true)
    {
        return new UserRole(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            roleId: roleId ?? Guid.NewGuid(),
            applicationId: applicationId,
            assignedAt: assignedAt ?? DateTime.UtcNow,
            assignedBy: assignedBy ?? SystemUserId,
            expiresAt: expiresAt,
            isActive: isActive);
    }

    /// <summary>
    /// Creates a test UserPermission entity.
    /// </summary>
    public static UserPermission CreateUserPermission(
        Guid? id = null,
        Guid? userId = null,
        Guid? permissionId = null,
        Guid? applicationId = null,
        DateTime? grantedAt = null,
        Guid? grantedBy = null,
        DateTime? expiresAt = null,
        bool isActive = true)
    {
        return new UserPermission(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            permissionId: permissionId ?? Guid.NewGuid(),
            applicationId: applicationId,
            grantedAt: grantedAt ?? DateTime.UtcNow,
            grantedBy: grantedBy ?? SystemUserId,
            expiresAt: expiresAt,
            isActive: isActive);
    }

    /// <summary>
    /// Creates a test PermissionImplication entity.
    /// </summary>
    public static PermissionImplication CreatePermissionImplication(
        Guid? id = null,
        Guid? permissionId = null,
        Guid? impliedPermissionId = null,
        DateTime? createdAt = null,
        Guid? createdBy = null)
    {
        return new PermissionImplication(
            id: id ?? Guid.NewGuid(),
            permissionId: permissionId ?? Guid.NewGuid(),
            impliedPermissionId: impliedPermissionId ?? Guid.NewGuid(),
            createdAt: createdAt ?? DateTime.UtcNow,
            createdBy: createdBy ?? SystemUserId);
    }

    /// <summary>
    /// Creates a test UserExternalLogin entity.
    /// </summary>
    public static UserExternalLogin CreateUserExternalLogin(
        Guid? id = null,
        Guid? userId = null,
        string? provider = null,
        string? providerUserId = null,
        string? email = null,
        string? name = null,
        string? pictureUrl = null,
        DateTime? createdAt = null,
        DateTime? modifiedAt = null)
    {
        return new UserExternalLogin(
            id: id ?? Guid.NewGuid(),
            userId: userId ?? Guid.NewGuid(),
            provider: provider ?? "google",
            providerUserId: providerUserId ?? Guid.NewGuid().ToString(),
            email: email ?? "external@test.com",
            name: name ?? "External User",
            pictureUrl: pictureUrl,
            createdAt: createdAt ?? DateTime.UtcNow,
            modifiedAt: modifiedAt);
    }

    /// <summary>
    /// Creates a test ExternalAuthProvider entity.
    /// </summary>
    public static ExternalAuthProvider CreateExternalAuthProvider(
        Guid? id = null,
        string? code = null,
        string? name = null,
        string? iconUrl = null,
        bool isEnabled = true,
        int displayOrder = 1,
        DateTime? createdAt = null,
        DateTime? modifiedAt = null)
    {
        return new ExternalAuthProvider(
            id: id ?? Guid.NewGuid(),
            code: code ?? "google",
            name: name ?? "Google",
            iconUrl: iconUrl,
            isEnabled: isEnabled,
            displayOrder: displayOrder,
            createdAt: createdAt ?? DateTime.UtcNow,
            modifiedAt: modifiedAt);
    }

    /// <summary>
    /// Creates IOptions wrapper for any settings type.
    /// </summary>
    public static IOptions<T> CreateOptions<T>(T value) where T : class
        => Options.Create(value);

    /// <summary>
    /// Creates an <see cref="IPasswordBreachEvaluator"/> that always allows the password
    /// (the breached-password check disabled / no-op), for handler tests not exercising that policy.
    /// </summary>
    public static IPasswordBreachEvaluator CreatePassingBreachEvaluator()
    {
        var mock = new Mock<IPasswordBreachEvaluator>();
        mock.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        return mock.Object;
    }

    /// <summary>
    /// Creates test PasswordSettings with sensible defaults.
    /// </summary>
    public static PasswordSettings CreatePasswordSettings(
        int minimumLength = 8,
        bool requireUppercase = true,
        bool requireLowercase = true,
        bool requireDigit = true,
        bool requireSpecialCharacter = true,
        int historyCount = 5,
        int maxFailedAttempts = 5,
        int lockoutDurationMinutes = 15)
    {
        return new PasswordSettings
        {
            MinimumLength = minimumLength,
            RequireUppercase = requireUppercase,
            RequireLowercase = requireLowercase,
            RequireDigit = requireDigit,
            RequireSpecialCharacter = requireSpecialCharacter,
            HistoryCount = historyCount,
            MaxFailedAttempts = maxFailedAttempts,
            LockoutDurationMinutes = lockoutDurationMinutes
        };
    }

    /// <summary>
    /// Creates test SessionSettings with sensible defaults.
    /// </summary>
    public static SessionSettings CreateSessionSettings(
        int lifetimeHours = 24,
        int maxConcurrentSessions = 5,
        bool terminateSessionsOnPasswordChange = true,
        bool terminateSessionsOnPasswordReset = true)
    {
        return new SessionSettings
        {
            LifetimeHours = lifetimeHours,
            MaxConcurrentSessions = maxConcurrentSessions,
            TerminateSessionsOnPasswordChange = terminateSessionsOnPasswordChange,
            TerminateSessionsOnPasswordReset = terminateSessionsOnPasswordReset
        };
    }

    /// <summary>
    /// Creates test GatewaySettings with sensible defaults.
    /// </summary>
    public static GatewaySettings CreateGatewaySettings(
        bool validationEnabled = true,
        string expectedToken = "test-gateway-token",
        string tokenHeaderName = "X-Gateway-Token",
        string[]? exemptPaths = null)
    {
        return new GatewaySettings
        {
            ValidationEnabled = validationEnabled,
            ExpectedToken = expectedToken,
            TokenHeaderName = tokenHeaderName,
            ExemptPaths = exemptPaths ?? new[] { "/.well-known/", "/health" }
        };
    }
}
