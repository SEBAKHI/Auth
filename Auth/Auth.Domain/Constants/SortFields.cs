namespace Auth.Domain.Constants;

/// <summary>
/// Allow-listed sort field names (camelCase, as clients send them) for every
/// list endpoint. Single source of truth shared by the Application validators
/// and the repository ORDER BY maps — client input never reaches SQL directly.
/// </summary>
public static class SortFields
{
    public static class Users
    {
        public const string Name = "name";
        public const string Email = "email";
        public const string Status = "status";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";
        public const string LastLoginAt = "lastLoginAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Email, Status, CreatedAt, ModifiedAt, LastLoginAt];
    }

    public static class Applications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string ContactEmail = "contactEmail";
        public const string Status = "status";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, ContactEmail, Status, CreatedAt, ModifiedAt];
    }

    public static class AuditLogs
    {
        public const string Action = "action";
        public const string EntityType = "entityType";
        public const string Timestamp = "timestamp";
        public const string Actor = "actor";

        public static readonly IReadOnlyList<string> Allowed =
            [Action, EntityType, Timestamp, Actor];
    }

    public static class OrganizationMembers
    {
        public const string Name = "name";
        public const string Email = "email";
        public const string RoleName = "roleName";
        public const string JoinedAt = "joinedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Email, RoleName, JoinedAt];
    }

    public static class Roles
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, CreatedAt, ModifiedAt];
    }

    public static class Permissions
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Level = "level";
        public const string CreatedAt = "createdAt";
        public const string ModifiedAt = "modifiedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, Level, CreatedAt, ModifiedAt];
    }

    public static class ApiKeys
    {
        public const string Name = "name";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";
        public const string RevokedAt = "revokedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, CreatedAt, ExpiresAt, RevokedAt];
    }

    public static class WebhookKeys
    {
        public const string Name = "name";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";
        public const string RevokedAt = "revokedAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, CreatedAt, ExpiresAt, RevokedAt];
    }

    public static class Sessions
    {
        public const string CreatedAt = "createdAt";
        public const string LastActivityAt = "lastActivityAt";
        public const string IpAddress = "ipAddress";

        public static readonly IReadOnlyList<string> Allowed =
            [CreatedAt, LastActivityAt, IpAddress];
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
        public const string ApplicationName = "applicationName";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
            [RoleName, ApplicationName, CreatedAt];
    }

    public static class UserPermissions
    {
        public const string PermissionName = "permissionName";
        public const string ApplicationName = "applicationName";
        public const string CreatedAt = "createdAt";

        public static readonly IReadOnlyList<string> Allowed =
            [PermissionName, ApplicationName, CreatedAt];
    }

    public static class PermissionImplications
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string Level = "level";

        public static readonly IReadOnlyList<string> Allowed = [Name, Code, Level];
    }

    public static class UserOrganizations
    {
        public const string Name = "name";
        public const string Code = "code";
        public const string RoleName = "roleName";
        public const string MemberCount = "memberCount";

        public static readonly IReadOnlyList<string> Allowed =
            [Name, Code, RoleName, MemberCount];
    }

    public static class OrganizationInvitations
    {
        public const string Email = "email";
        public const string RoleName = "roleName";
        public const string Status = "status";
        public const string CreatedAt = "createdAt";
        public const string ExpiresAt = "expiresAt";

        public static readonly IReadOnlyList<string> Allowed =
            [Email, RoleName, Status, CreatedAt, ExpiresAt];
    }

    public static class OrganizationApplications
    {
        public const string ApplicationName = "applicationName";
        public const string ApplicationCode = "applicationCode";
        public const string IsActive = "isActive";

        public static readonly IReadOnlyList<string> Allowed =
            [ApplicationName, ApplicationCode, IsActive];
    }
}
