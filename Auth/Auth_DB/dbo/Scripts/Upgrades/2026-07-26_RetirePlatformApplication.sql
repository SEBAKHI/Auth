-- Retire the seeded platform application row for EXISTING databases (dev/prod).
--
-- Background: the "Auth System" application (Id 00000000-0000-0000-0000-000000000001,
-- Code 'auth') was seeded as if the identity platform were a tenant of itself. Nothing
-- depends on it: first-party console/accounts login is app-less (ApplicationId = NULL
-- everywhere), console branding comes from PlatformSettings, gateway auth uses the
-- shared X-Gateway-Token secret, and permission checks match permission codes only.
-- The Applications table is for EXTERNAL client applications.
--
-- This script moves every platform RBAC row scoped to that application to the global
-- (NULL) scope, removes its credential/session children, and finally deletes the row.
-- It MUST run before the seed steps in Script.PostDeployment.sql:
--  * The role seed guards now check Code + ApplicationId IS NULL. Without the re-scope
--    below, on an existing DB the guards would find nothing and re-INSERT the hardcoded
--    PKs 10000000-...-0002/3/4 => PK violation on every redeploy.
--  * The permission seed guards are Code-only and would silently skip, leaving stale
--    app-scoped rows behind (including rows historically seeded by the retired
--    08_AdditionalPermissions.sql, which exist only in deployed databases).
--
-- Idempotent: on fresh installs and on every run after the first, all statements
-- affect 0 rows. This script is intentionally part of every database publish.

-- Several touched tables carry filtered indexes, and any DML against them requires
-- QUOTED_IDENTIFIER ON. SqlClient sets it by default; sqlcmd does NOT, so set it here
-- or the statements fail with "SET options have incorrect settings" (Msg 1934).
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

-- Same literal, two identities: the retired application row and the seeded system user.
DECLARE @AuthAppId    UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @Rows INT;

-- 1) Permissions: blanket re-scope by ApplicationId, not a fixed Id list — deployed
--    databases hold rows from the retired 08_AdditionalPermissions.sql with Ids this
--    script cannot enumerate. UQ_Permissions_Code is on [Code] alone, so moving rows
--    to the NULL scope can never collide.
UPDATE [dbo].[Permissions]
SET [ApplicationId] = NULL,
    [ModifiedAt]    = GETUTCDATE(),
    [ModifiedBy]    = @SystemUserId
WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Re-scoped platform Permissions to global: ' + CAST(@Rows AS NVARCHAR(20));

-- 2) Roles: UQ_Roles_Code_Application treats NULLs as equal, so a row whose Code
--    already exists at the NULL scope is a functional duplicate — delete it (with its
--    grants) instead of colliding; re-scope the rest.
DELETE rp
FROM [dbo].[RolePermissions] rp
INNER JOIN [dbo].[Roles] r ON r.[Id] = rp.[RoleId]
WHERE r.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[Roles] t
              WHERE t.[Code] = r.[Code] AND t.[ApplicationId] IS NULL);

DELETE ur
FROM [dbo].[UserRoles] ur
INNER JOIN [dbo].[Roles] r ON r.[Id] = ur.[RoleId]
WHERE r.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[Roles] t
              WHERE t.[Code] = r.[Code] AND t.[ApplicationId] IS NULL);

DELETE r
FROM [dbo].[Roles] r
WHERE r.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[Roles] t
              WHERE t.[Code] = r.[Code] AND t.[ApplicationId] IS NULL);
SET @Rows = @@ROWCOUNT;
IF @Rows > 0
    PRINT 'Deleted platform Roles duplicated at the global scope: ' + CAST(@Rows AS NVARCHAR(20));

UPDATE [dbo].[Roles]
SET [ApplicationId] = NULL,
    [ModifiedAt]    = GETUTCDATE(),
    [ModifiedBy]    = @SystemUserId
WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Re-scoped platform Roles to global: ' + CAST(@Rows AS NVARCHAR(20));

-- 3) Direct assignments carrying the app scope (none are expected: seeded admin
--    assignments are global and first-party login writes NULL). Delete rows that
--    already have a global twin (UQ_UserRoles/UQ_UserPermissions would collide),
--    then re-scope the rest.
DELETE ur
FROM [dbo].[UserRoles] ur
WHERE ur.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[UserRoles] t
              WHERE t.[UserId] = ur.[UserId] AND t.[RoleId] = ur.[RoleId]
                AND t.[ApplicationId] IS NULL);

UPDATE [dbo].[UserRoles]
SET [ApplicationId] = NULL
WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Re-scoped app-scoped UserRoles to global: ' + CAST(@Rows AS NVARCHAR(20));

DELETE up
FROM [dbo].[UserPermissions] up
WHERE up.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[UserPermissions] t
              WHERE t.[UserId] = up.[UserId] AND t.[PermissionId] = up.[PermissionId]
                AND t.[ApplicationId] IS NULL);

UPDATE [dbo].[UserPermissions]
SET [ApplicationId] = NULL
WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Re-scoped app-scoped UserPermissions to global: ' + CAST(@Rows AS NVARCHAR(20));

-- 4) Credentials owned by the retired application. Revoked rows still FK-block the
--    delete, so they must go; the audit trail lives in AuditLogs, which has no FK.
DELETE s
FROM [dbo].[ApiKeyScopes] s
WHERE s.[ApiKeyId] IN (SELECT [Id] FROM [dbo].[ApiKeys] WHERE [ApplicationId] = @AuthAppId);

DELETE FROM [dbo].[ApiKeys] WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Deleted platform-app ApiKeys: ' + CAST(@Rows AS NVARCHAR(20));

DELETE FROM [dbo].[WebhookKeys] WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Deleted platform-app WebhookKeys: ' + CAST(@Rows AS NVARCHAR(20));

-- 5) Organization links to the retired application.
DELETE FROM [dbo].[OrganizationUserPermissions] WHERE [ApplicationId] = @AuthAppId;
DELETE FROM [dbo].[OrganizationUserRoles]       WHERE [ApplicationId] = @AuthAppId;
DELETE FROM [dbo].[OrganizationApplications]    WHERE [ApplicationId] = @AuthAppId;

-- 6) App-scoped sessions and refresh tokens are DELETED, not re-scoped: a refresh
--    token whose ApplicationId becomes NULL would mint platform-audience access
--    tokens on rotation — the exact privilege escalation the refresh handler now
--    rejects. No first-party session ever carries this ApplicationId.
DELETE FROM [dbo].[RefreshTokens] WHERE [ApplicationId] = @AuthAppId;
SET @Rows = @@ROWCOUNT;
PRINT 'Deleted app-scoped RefreshTokens: ' + CAST(@Rows AS NVARCHAR(20));

DELETE FROM [dbo].[UserSessions] WHERE [ApplicationId] = @AuthAppId;

-- 7) Historical rows keep their history, dropping only the app reference.
UPDATE [dbo].[LoginAttempts]      SET [ApplicationId] = NULL WHERE [ApplicationId] = @AuthAppId;
UPDATE [dbo].[NotificationOutbox] SET [ApplicationId] = NULL WHERE [ApplicationId] = @AuthAppId;

-- App-scoped notification templates/layouts are unexpected (all seeds are global).
-- Re-scoping one would collide with the global row on UQ_NotificationTemplates_Resolution
-- / UQ_NotificationLayouts_App_Channel, so a duplicate is deleted instead.
DELETE nt
FROM [dbo].[NotificationTemplates] nt
WHERE nt.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] t
              WHERE t.[ApplicationId] IS NULL
                AND t.[NotificationTypeId] = nt.[NotificationTypeId]
                AND t.[Channel] = nt.[Channel]);
UPDATE [dbo].[NotificationTemplates] SET [ApplicationId] = NULL WHERE [ApplicationId] = @AuthAppId;

DELETE nl
FROM [dbo].[NotificationLayouts] nl
WHERE nl.[ApplicationId] = @AuthAppId
  AND EXISTS (SELECT 1 FROM [dbo].[NotificationLayouts] t
              WHERE t.[ApplicationId] IS NULL AND t.[Channel] = nl.[Channel]);
UPDATE [dbo].[NotificationLayouts] SET [ApplicationId] = NULL WHERE [ApplicationId] = @AuthAppId;

-- 8) Finally delete the application row itself. AuthorizationCodes and
--    ApplicationRedirectUris cascade. Guarded so an unexpected leftover reference
--    turns into a publish WARNING instead of a failed deployment; the next publish
--    retries after manual cleanup.
IF EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [Id] = @AuthAppId)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[ApiKeys]                     WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[WebhookKeys]                 WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[Roles]                       WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[Permissions]                 WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles]                   WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[UserPermissions]             WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[RefreshTokens]               WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[UserSessions]                WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[LoginAttempts]               WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[OrganizationApplications]    WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[OrganizationUserRoles]       WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[OrganizationUserPermissions] WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates]       WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationLayouts]         WHERE [ApplicationId] = @AuthAppId)
   AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationOutbox]          WHERE [ApplicationId] = @AuthAppId)
    BEGIN
        DELETE FROM [dbo].[Applications] WHERE [Id] = @AuthAppId;
        PRINT 'Deleted the retired platform application row.';
    END
    ELSE
        PRINT 'WARNING: the retired platform application still has referencing rows; '
            + 'row NOT deleted. Inspect the tables above and re-publish. See release runbook.';
END
ELSE
    PRINT 'Platform application row already absent.';
GO
