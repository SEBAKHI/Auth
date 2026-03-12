using Auth.Domain.Entities;
using Auth.Domain.Enums;

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
}
