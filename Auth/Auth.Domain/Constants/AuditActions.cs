namespace Auth.Domain.Constants;

/// <summary>
/// The category an audit row is filed under. Nine of them, and the list is
/// closed: <see cref="AuditActions.ByCode"/> assigns every action to exactly one.
/// </summary>
/// <remarks>
/// These strings reach <c>dbo.AuditLogs.ActionType</c> unchanged. Renaming one
/// orphans every row already written under the old name, so a change here is a
/// data migration, not a rename.
/// </remarks>
public static class AuditActionTypes
{
    /// <summary>Signing in and signing out.</summary>
    public const string Authentication = "Authentication";

    /// <summary>Who may do what: roles, permissions, and their grants.</summary>
    public const string Authorization = "Authorization";

    /// <summary>Credentials, second factors, lockouts, keys, sessions.</summary>
    public const string Security = "Security";

    /// <summary>The lifecycle of an account, up to and including its destruction.</summary>
    public const string UserManagement = "UserManagement";

    /// <summary>Operator changes to the platform itself.</summary>
    public const string Administration = "Administration";

    /// <summary>Registered client applications and access to them.</summary>
    public const string Application = "Application";

    /// <summary>Organizations and who owns them.</summary>
    public const string OrganizationManagement = "OrganizationManagement";

    /// <summary>Issuing and revoking API keys.</summary>
    public const string ApiKeyManagement = "ApiKeyManagement";

    /// <summary>Actions with no human actor: workers, sweeps, publications.</summary>
    public const string System = "System";

    /// <summary>Every category, in the order the console presents them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Authentication,
        Authorization,
        Security,
        UserManagement,
        Administration,
        Application,
        OrganizationManagement,
        ApiKeyManagement,
        System
    ];
}

/// <summary>
/// Every action this system records, and the category each is filed under.
/// The single source of truth for both halves of an audit row.
/// </summary>
/// <remarks>
/// <para>
/// Before this class the two values were string literals repeated across
/// forty-nine call sites. Nothing held them together: an action could be filed
/// under one category in the handler that wrote it and another in the handler
/// beside it, and no compiler or test would say so.
/// </para>
/// <para>
/// Worse for anyone taking an inventory, four of the codes are produced by a
/// conditional rather than a literal (<c>application.activated</c> /
/// <c>application.deactivated</c> and <c>user.logout</c> /
/// <c>user.logout.all</c>), so a list built by searching for quoted strings
/// silently misses them. The first inventory written for this change did exactly
/// that and came back four short, sign-out among them.
/// </para>
/// <para>
/// The codes themselves are unchanged from what the handlers already wrote, to
/// the character. Production rows and new rows have to stay filterable by the
/// same value, so this class names existing strings rather than choosing better
/// ones. <c>AuditCatalogCoverageTests</c> holds the call sites and this
/// catalogue together in both directions.
/// </para>
/// </remarks>
public static class AuditActions
{
    // Authentication
    public const string UserLogin = "user.login";

    /// <summary>Signed out of one device.</summary>
    public const string UserLogout = "user.logout";

    /// <summary>
    /// Signed out everywhere. A separate code from <see cref="UserLogout"/>
    /// because the two answer different questions while reading an incident.
    /// </summary>
    public const string UserLogoutAll = "user.logout.all";

    // Authorization
    public const string PermissionGranted = "permission.granted";
    public const string PermissionRevoked = "permission.revoked";
    public const string RoleAssigned = "role.assigned";
    public const string RoleRemoved = "role.removed";
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string RolePermissionGranted = "role.permission.granted";
    public const string RolePermissionRevoked = "role.permission.revoked";

    // Security
    public const string PasswordCreated = "password.created";
    public const string PasswordChanged = "password.changed";
    public const string TwoFactorEnabled = "twofactor.enabled";
    public const string TwoFactorDisabled = "twofactor.disabled";
    public const string UserLocked = "user.locked";
    public const string UserUnlocked = "user.unlocked";
    public const string ExternalLoginLinked = "external-login.linked";
    public const string SessionEnded = "session.ended";
    public const string WebhookKeyCreated = "webhookkey.created";
    public const string WebhookKeyRevoked = "webhookkey.revoked";

    // UserManagement
    public const string UserCreated = "user.created";
    public const string UserDeleted = "user.deleted";

    /// <summary>An administrator destroyed the account outright rather than
    /// soft-deleting it.</summary>
    public const string UserHardDeleted = "user.harddeleted";
    public const string UserDeletionRequested = "user.deletion_requested";
    public const string UserDeletionCancelled = "user.deletion_cancelled";
    public const string UserDeletionCompleted = "user.deletion_completed";

    /// <summary>A deletion whose execution failed and was handed back to the
    /// worker for another attempt.</summary>
    public const string UserDeletionReapplied = "user.deletion_reapplied";

    // Administration
    public const string SystemSettingsUpdated = "system-settings.updated";
    public const string PlatformSettingsUpdated = "platform-settings.updated";
    public const string NotificationTemplatePublished = "notification-template.published";
    public const string NotificationTemplateUnpublished = "notification-template.unpublished";
    public const string NotificationTemplateRolledBack = "notification-template.rolled-back";
    public const string SecretsValueChanged = "secrets.value.changed";
    public const string SecretsOperationConfirmationRequested = "secrets.operation.confirmation-requested";
    public const string SecretsOperationExecuted = "secrets.operation.executed";

    // Application
    public const string ApplicationAccessGranted = "application.access.granted";
    public const string ApplicationAccessRevoked = "application.access.revoked";
    public const string ApplicationActivated = "application.activated";
    public const string ApplicationDeactivated = "application.deactivated";

    // OrganizationManagement
    public const string OrganizationOwnershipTransferInitiated = "organization.ownership_transfer_initiated";
    public const string OrganizationOwnershipTransferred = "organization.ownership_transferred";

    // ApiKeyManagement
    public const string ApiKeyCreated = "apikey.created";
    public const string ApiKeyRevoked = "apikey.revoked";

    // System
    public const string SystemPrivacyPolicyContentSaved = "system.privacy_policy_content_saved";
    public const string SystemPrivacyPolicyPublished = "system.privacy_policy_published";
    public const string SystemPolicyNotificationSent = "system.policy_notification_sent";
    public const string SystemRetentionSweep = "system.retention_sweep";

    /// <summary>
    /// Every action, mapped to the single category it is filed under.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison, not case-insensitive: these codes are written by this
    /// process alone and are always lower case. Accepting a differently-cased
    /// spelling would let two spellings of one action both look valid here while
    /// the database treats them as two rows and the console as two actions.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> ByCode =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserLogin] = AuditActionTypes.Authentication,
            [UserLogout] = AuditActionTypes.Authentication,
            [UserLogoutAll] = AuditActionTypes.Authentication,

            [PermissionGranted] = AuditActionTypes.Authorization,
            [PermissionRevoked] = AuditActionTypes.Authorization,
            [RoleAssigned] = AuditActionTypes.Authorization,
            [RoleRemoved] = AuditActionTypes.Authorization,
            [RoleCreated] = AuditActionTypes.Authorization,
            [RoleUpdated] = AuditActionTypes.Authorization,
            [RoleDeleted] = AuditActionTypes.Authorization,
            [RolePermissionGranted] = AuditActionTypes.Authorization,
            [RolePermissionRevoked] = AuditActionTypes.Authorization,

            [PasswordCreated] = AuditActionTypes.Security,
            [PasswordChanged] = AuditActionTypes.Security,
            [TwoFactorEnabled] = AuditActionTypes.Security,
            [TwoFactorDisabled] = AuditActionTypes.Security,
            [UserLocked] = AuditActionTypes.Security,
            [UserUnlocked] = AuditActionTypes.Security,
            [ExternalLoginLinked] = AuditActionTypes.Security,
            [SessionEnded] = AuditActionTypes.Security,
            [WebhookKeyCreated] = AuditActionTypes.Security,
            [WebhookKeyRevoked] = AuditActionTypes.Security,

            [UserCreated] = AuditActionTypes.UserManagement,
            [UserDeleted] = AuditActionTypes.UserManagement,
            [UserHardDeleted] = AuditActionTypes.UserManagement,
            [UserDeletionRequested] = AuditActionTypes.UserManagement,
            [UserDeletionCancelled] = AuditActionTypes.UserManagement,
            [UserDeletionCompleted] = AuditActionTypes.UserManagement,
            [UserDeletionReapplied] = AuditActionTypes.UserManagement,

            [SystemSettingsUpdated] = AuditActionTypes.Administration,
            [PlatformSettingsUpdated] = AuditActionTypes.Administration,
            [NotificationTemplatePublished] = AuditActionTypes.Administration,
            [NotificationTemplateUnpublished] = AuditActionTypes.Administration,
            [NotificationTemplateRolledBack] = AuditActionTypes.Administration,
            [SecretsValueChanged] = AuditActionTypes.Administration,
            [SecretsOperationConfirmationRequested] = AuditActionTypes.Administration,
            [SecretsOperationExecuted] = AuditActionTypes.Administration,

            [ApplicationAccessGranted] = AuditActionTypes.Application,
            [ApplicationAccessRevoked] = AuditActionTypes.Application,
            [ApplicationActivated] = AuditActionTypes.Application,
            [ApplicationDeactivated] = AuditActionTypes.Application,

            [OrganizationOwnershipTransferInitiated] = AuditActionTypes.OrganizationManagement,
            [OrganizationOwnershipTransferred] = AuditActionTypes.OrganizationManagement,

            [ApiKeyCreated] = AuditActionTypes.ApiKeyManagement,
            [ApiKeyRevoked] = AuditActionTypes.ApiKeyManagement,

            [SystemPrivacyPolicyContentSaved] = AuditActionTypes.System,
            [SystemPrivacyPolicyPublished] = AuditActionTypes.System,
            [SystemPolicyNotificationSent] = AuditActionTypes.System,
            [SystemRetentionSweep] = AuditActionTypes.System
        };

    /// <summary>Every action code, in catalogue order.</summary>
    public static readonly IReadOnlyList<string> All = [.. ByCode.Keys];
}
