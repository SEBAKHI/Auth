namespace Auth.Domain.Constants;

/// <summary>
/// Allow-listed sort field names (camelCase, as clients send them) for every
/// list endpoint. Single source of truth shared by the Application validators
/// and the repository ORDER BY maps — client input never reaches SQL directly.
/// Lists cover every DTO-exposed field that is safe and meaningful to sort:
/// excluded by design are secrets (key/password hashes), JSON blobs
/// (oldValues/newValues/details), GUID identifiers (random byte order),
/// and fields the API hard-codes rather than stores.
/// </summary>
public static class SortFields
{
    public static class Users
    {
        public const string Name = "name";
        public const string DisplayName = "displayName";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string Email = "email";
        public const string PhoneNumber = "phoneNumber";
        public const string Status = "status";
        public const string EmailConfirmed = "emailConfirmed";
        public const string PhoneConfirmed = "phoneConfirmed";
        public const string TwoFactorEnabled = "twoFactorEnabled";
        public const string PreferredLanguage = "preferredLanguage";
        public const string TimeZone = "timeZone";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";
        public const string LastLoginAt = "lastLoginAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, DisplayName, FirstName, LastName, Email, PhoneNumber, Status,
            EmailConfirmed, PhoneConfirmed, TwoFactorEnabled, PreferredLanguage,
            TimeZone, CreatedAt, ModifiedAt, LastLoginAt
        ];
    }

    public static class Organizations
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string ContactEmail = "contactEmail";
        public const string IsActive = "isActive";
        public const string MemberCount = "memberCount";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, Code, ContactEmail, IsActive, MemberCount, CreatedAt, ModifiedAt
        ];
    }

    public static class Applications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Description = "description";
        public const string BaseUrl = "baseUrl";
        public const string ContactEmail = "contactEmail";
        public const string Status = "status";
        public const string IsActive = "isActive";
        public const string AllowSelfRegistration = "allowSelfRegistration";
        public const string RequireTwoFactor = "requireTwoFactor";
        public const string RequireEmailVerification = "requireEmailVerification";
        public const string SessionTimeoutMinutes = "sessionTimeoutMinutes";
        public const string MaxConcurrentSessions = "maxConcurrentSessions";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, Code, Description, BaseUrl, ContactEmail, Status, IsActive,
            AllowSelfRegistration, RequireTwoFactor, RequireEmailVerification,
            SessionTimeoutMinutes, MaxConcurrentSessions, CreatedAt, ModifiedAt
        ];
    }

    public static class AuditLogs
    {
        public const string Action = "action";
        public const string EntityType = "entityType";
        public const string Timestamp = "timestamp";
        public const string Actor = "actor";
        public const string UserName = "userName";
        public const string UserEmail = "userEmail";
        public const string ApplicationName = "applicationName";
        public const string IpAddress = "ipAddress";
        public const string UserAgent = "userAgent";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Action, EntityType, Timestamp, Actor, UserName, UserEmail,
            ApplicationName, IpAddress, UserAgent
        ];
    }

    public static class OrganizationMembers
    {
        public const string Name = "name";
        public const string FullName = "fullName";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string Email = "email";
        public const string RoleName = "roleName";
        public const string RoleCode = "roleCode";
        public const string IsActive = "isActive";
        public const string JoinedAt = "joinedAt";
        public const string InvitedByName = "invitedByName";
        public const string ExpiresAt = "expiresAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, FullName, FirstName, LastName, Email, RoleName, RoleCode,
            IsActive, JoinedAt, InvitedByName, ExpiresAt
        ];
    }

    public static class Roles
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Description = "description";
        public const string IsSystem = "isSystem";
        public const string IsActive = "isActive";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, Description, IsSystem, IsActive, CreatedAt, ModifiedAt];
    }

    public static class Permissions
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Description = "description";
        public const string Level = "level";
        public const string IsWildcard = "isWildcard";
        public const string IsActive = "isActive";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, Description, Level, IsWildcard, IsActive, CreatedAt, ModifiedAt];
    }

    public static class ApiKeys
    {
        public const string Name = "name";
        public const string Description = "description";
        public const string KeyPrefix = "keyPrefix";
        public const string Environment = "environment";
        public const string RateLimitPerMinute = "rateLimitPerMinute";
        public const string RateLimitPerDay = "rateLimitPerDay";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";
        public const string LastUsedAt = "lastUsedAt";
        public const string RevokedAt = "revokedAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, Description, KeyPrefix, Environment, RateLimitPerMinute,
            RateLimitPerDay, CreatedAt, ExpiresAt, LastUsedAt, RevokedAt
        ];
    }

    public static class WebhookKeys
    {
        public const string Name = "name";
        public const string Description = "description";
        public const string KeyPrefix = "keyPrefix";
        public const string TargetUrl = "targetUrl";
        public const string Environment = "environment";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";
        public const string LastUsedAt = "lastUsedAt";
        public const string RevokedAt = "revokedAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Name, Description, KeyPrefix, TargetUrl, Environment,
            CreatedAt, ExpiresAt, LastUsedAt, RevokedAt
        ];
    }

    public static class Sessions
    {
        public const string CreatedAt = "createdAt";
        public const string LastActivityAt = "lastActivityAt";
        public const string ExpiresAt = "expiresAt";
        public const string IpAddress = "ipAddress";
        public const string UserAgent = "userAgent";
        public const string DeviceName = "deviceName";
        public const string Location = "location";

        public static readonly IReadOnlyList<string> Allowed =
            [CreatedAt, LastActivityAt, ExpiresAt, IpAddress, UserAgent, DeviceName, Location];
    }

    public static class ExternalProviders
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string DisplayOrder = "displayOrder";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, DisplayOrder];
    }

    public static class UserRoles
    {
        public const string RoleName = "roleName";
        public const string RoleCode = "roleCode";
        public const string RoleDescription = "roleDescription";
        public const string ApplicationName = "applicationName";
        public const string ApplicationCode = "applicationCode";
        public const string IsActive = "isActive";
        public const string ExpiresAt = "expiresAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            RoleName, RoleCode, RoleDescription, ApplicationName,
            ApplicationCode, IsActive, ExpiresAt, CreatedAt
        ];
    }

    public static class UserPermissions
    {
        public const string PermissionName = "permissionName";
        public const string PermissionCode = "permissionCode";
        public const string PermissionDescription = "permissionDescription";
        public const string ApplicationName = "applicationName";
        public const string ApplicationCode = "applicationCode";
        public const string IsActive = "isActive";
        public const string ExpiresAt = "expiresAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            PermissionName, PermissionCode, PermissionDescription,
            ApplicationName, ApplicationCode, IsActive, ExpiresAt, CreatedAt
        ];
    }

    public static class PermissionImplications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Description = "description";
        public const string Level = "level";
        public const string IsWildcard = "isWildcard";
        public const string IsActive = "isActive";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, Description, Level, IsWildcard, IsActive, CreatedAt, ModifiedAt];
    }

    public static class UserOrganizations
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string RoleName = "roleName";
        public const string MemberCount = "memberCount";
        public const string IsActive = "isActive";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, RoleName, MemberCount, IsActive];
    }

    public static class OrganizationInvitations
    {
        public const string Email = "email";
        public const string RoleName = "roleName";
        public const string RoleCode = "roleCode";
        public const string Status = "status";
        public const string IsExpired = "isExpired";
        public const string InvitedByName = "invitedByName";
        public const string InvitedByEmail = "invitedByEmail";
        public const string AcceptedAt = "acceptedAt";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            Email, RoleName, RoleCode, Status, IsExpired, InvitedByName,
            InvitedByEmail, AcceptedAt, CreatedAt, ExpiresAt
        ];
    }

    public static class OrganizationApplications
    {
        public const string ApplicationName = "applicationName";
        public const string ApplicationCode = "applicationCode";
        public const string ApplicationDescription = "applicationDescription";
        public const string SubscriptionTier = "subscriptionTier";
        public const string EnabledAt = "enabledAt";
        public const string ExpiresAt = "expiresAt";
        public const string IsActive = "isActive";
        public const string AssignedUserCount = "assignedUserCount";

        public static readonly IReadOnlyList<string> Allowed =
        [
            ApplicationName, ApplicationCode, ApplicationDescription,
            SubscriptionTier, EnabledAt, ExpiresAt, IsActive, AssignedUserCount
        ];
    }

    public static class UserApplications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string IsActive = "isActive";
        public const string AccessSource = "accessSource";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, IsActive, AccessSource];
    }

    public static class ApplicationUsers
    {
        public const string Email = "email";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string DisplayName = "displayName";
        public const string Status = "status";
        public const string LastLoginAt = "lastLoginAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Email, FirstName, LastName, DisplayName, Status, LastLoginAt, CreatedAt];
    }

    public static class ApplicationOrganizations
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string EnabledAt = "enabledAt";
        public const string ExpiresAt = "expiresAt";
        public const string IsActive = "isActive";
        public const string OrganizationIsActive = "organizationIsActive";
        public const string MemberCount = "memberCount";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, EnabledAt, ExpiresAt, IsActive, OrganizationIsActive, MemberCount];
    }

    public static class RoleUsers
    {
        public const string Email = "email";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string DisplayName = "displayName";
        public const string Status = "status";
        public const string LastLoginAt = "lastLoginAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Email, FirstName, LastName, DisplayName, Status, LastLoginAt, CreatedAt];
    }

    public static class RoleApplications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string IsActive = "isActive";
        public const string Relationship = "relationship";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, IsActive, Relationship];
    }

    public static class NotificationTemplates
    {
        public const string TypeName = "typeName";
        public const string TypeCode = "typeCode";
        public const string ApplicationName = "applicationName";
        public const string Channel = "channel";
        public const string DefaultLanguage = "defaultLanguage";
        public const string PublishedVersionNumber = "publishedVersionNumber";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            TypeName, TypeCode, ApplicationName, Channel, DefaultLanguage,
            PublishedVersionNumber, CreatedAt, ModifiedAt
        ];
    }

    public static class NotificationOutbox
    {
        public const string TypeCode = "typeCode";
        public const string Recipient = "recipient";
        public const string LanguageCode = "languageCode";
        public const string Status = "status";
        public const string AttemptCount = "attemptCount";
        public const string NextAttemptAt = "nextAttemptAt";
        public const string SentAt = "sentAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
        [
            TypeCode, Recipient, LanguageCode, Status, AttemptCount,
            NextAttemptAt, SentAt, CreatedAt
        ];
    }

    public static class PermissionUsers
    {
        public const string Email = "email";
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string DisplayName = "displayName";
        public const string Status = "status";
        public const string LastLoginAt = "lastLoginAt";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Email, FirstName, LastName, DisplayName, Status, LastLoginAt, CreatedAt];
    }
}
