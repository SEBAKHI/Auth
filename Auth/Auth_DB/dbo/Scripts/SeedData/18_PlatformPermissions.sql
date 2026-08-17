-- Platform Permissions Seed Data
--
-- The 34 permission codes the API enforces that had no row anywhere on the
-- executed publish path, plus the seven area wildcards they hang from, plus the
-- grants that make the built-in roles mean something.
--
-- WHY THIS FILE EXISTS
--
-- The inline seeds in Script.PostDeployment.sql write an "auth:"-prefixed
-- hierarchy (auth:users:read, auth:roles:*, ...) while every controller enforces
-- a SHORT code (users:read, roles:read, ...). Wildcard resolution is a
-- left-anchored STRING prefix, not a walk through Permissions.ParentId, so
-- holding auth:users:* satisfies auth:users:read and nothing else. The result:
-- the admin, user-manager and auditor roles granted exactly zero of the codes
-- the API asks for, and only super-admin's "*" could operate the console. The
-- missing rows also made the gap unfixable by hand, because a code with no row
-- cannot be granted through the console at all.
--
-- 08_AdditionalPermissions.sql was written for this and never :r-included. It
-- is left where it is, unused: every row it inserts is stamped with the
-- Applications id that 2026-07-26_RetirePlatformApplication.sql deletes earlier
-- in the same publish, so including it would fail FK_Permissions_Applications
-- and abort everything after it.
--
-- CONVENTIONS (matching 13/15/17)
--   * ApplicationId is a literal NULL: these are platform permissions, and
--     PlatformSeedContractTests forbids @AuthAppId in the post-deploy text.
--   * Idempotency is a guarded INSERT keyed on [Code], never a MERGE. Repeat
--     publishes skip; UQ_Permissions_Code is on [Code] alone so this cannot
--     duplicate.
--   * Grants are INSERT ... SELECT resolving [PermissionId] BY CODE rather than
--     by a hardcoded id. A database that already carries these codes from a
--     hand-run of 08 holds them under 08's ids, so a hardcoded id would either
--     violate the foreign key or silently grant nothing.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @Root UNIQUEIDENTIFIER = '20000000-0000-0000-0000-000000000001'; -- "*"

-- ============================================================
-- Area wildcards (Level 1, children of "*")
-- ============================================================
-- These are what the role grants below actually reference. A role holding
-- users:* covers every present and future users:<x> gate by prefix, which is
-- the property that stops this file needing an edit for each new endpoint.

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000200', N'users:*', N'All User Permissions', N'Full user administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000201', N'roles:*', N'All Role Permissions', N'Full role administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000202', N'permissions:*', N'All Permission Permissions', N'Full permission administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000203', N'applications:*', N'All Application Permissions', N'Full application administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auditlogs:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000204', N'auditlogs:*', N'All Audit Log Permissions', N'Full audit log access across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auditlogs:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000205', N'apikeys:*', N'All API Key Permissions', N'Full API key administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:* permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'webhookkeys:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000206', N'webhookkeys:*', N'All Webhook Key Permissions', N'Full webhook key administration across the platform', NULL, @Root, 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created webhookkeys:* permission';
END
GO

-- ============================================================
-- Leaf permissions (Level 2, one per enforced code)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @Leaves TABLE (
    [Id] UNIQUEIDENTIFIER,
    [Code] NVARCHAR(200),
    [Name] NVARCHAR(200),
    [Description] NVARCHAR(500),
    [ParentCode] NVARCHAR(200),
    [Level] TINYINT);

INSERT INTO @Leaves ([Id], [Code], [Name], [Description], [ParentCode], [Level]) VALUES
    -- Users
    (N'20000000-0000-0000-0000-000000000210', N'users:read',                N'View Users',               N'View users and their details',                              N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000211', N'users:create',              N'Create Users',             N'Create new user accounts',                                  N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000212', N'users:update',              N'Update Users',             N'Change user details',                                       N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000213', N'users:delete',              N'Delete Users',             N'Delete user accounts',                                      N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000214', N'users:manage',              N'Manage Users',             N'Lock, unlock, restore and see deleted accounts',            N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000215', N'users:manage-roles',        N'Manage User Roles',        N'Assign and remove roles on a user',                         N'users:*', 2),
    (N'20000000-0000-0000-0000-000000000216', N'users:manage-permissions',  N'Manage User Permissions',  N'Grant and revoke permissions directly on a user',           N'users:*', 2),
    -- Roles
    (N'20000000-0000-0000-0000-000000000220', N'roles:read',                N'View Roles',               N'View roles and their permissions',                          N'roles:*', 2),
    (N'20000000-0000-0000-0000-000000000221', N'roles:create',              N'Create Roles',             N'Create new roles',                                          N'roles:*', 2),
    (N'20000000-0000-0000-0000-000000000222', N'roles:update',              N'Update Roles',             N'Change role details',                                       N'roles:*', 2),
    (N'20000000-0000-0000-0000-000000000223', N'roles:delete',              N'Delete Roles',             N'Delete roles',                                              N'roles:*', 2),
    -- Permissions
    (N'20000000-0000-0000-0000-000000000230', N'permissions:read',          N'View Permissions',         N'View the permission catalogue',                             N'permissions:*', 2),
    (N'20000000-0000-0000-0000-000000000231', N'permissions:create',        N'Create Permissions',       N'Define new permissions',                                    N'permissions:*', 2),
    (N'20000000-0000-0000-0000-000000000232', N'permissions:update',        N'Update Permissions',       N'Change permission details',                                 N'permissions:*', 2),
    (N'20000000-0000-0000-0000-000000000233', N'permissions:delete',        N'Delete Permissions',       N'Delete permissions',                                        N'permissions:*', 2),
    (N'20000000-0000-0000-0000-000000000234', N'permissions:manage',        N'Manage Permissions',       N'Manage permission implications',                            N'permissions:*', 2),
    -- Applications
    (N'20000000-0000-0000-0000-000000000240', N'applications:read',         N'View Applications',        N'View registered applications',                              N'applications:*', 2),
    (N'20000000-0000-0000-0000-000000000241', N'applications:create',       N'Create Applications',      N'Register new applications',                                 N'applications:*', 2),
    (N'20000000-0000-0000-0000-000000000242', N'applications:update',       N'Update Applications',      N'Change application settings and secrets',                   N'applications:*', 2),
    (N'20000000-0000-0000-0000-000000000243', N'applications:delete',       N'Delete Applications',      N'Delete applications',                                       N'applications:*', 2),
    -- Audit logs
    (N'20000000-0000-0000-0000-000000000250', N'auditlogs:read',            N'View Audit Logs',          N'Read the audit log',                                        N'auditlogs:*', 2),
    (N'20000000-0000-0000-0000-000000000251', N'auditlogs:export',          N'Export Audit Logs',        N'Export the audit log in bulk',                              N'auditlogs:*', 2),
    -- API keys
    (N'20000000-0000-0000-0000-000000000260', N'apikeys:read',              N'View API Keys',            N'View API keys and their metadata',                          N'apikeys:*', 2),
    (N'20000000-0000-0000-0000-000000000261', N'apikeys:create',            N'Create API Keys',          N'Issue new API keys',                                        N'apikeys:*', 2),
    (N'20000000-0000-0000-0000-000000000262', N'apikeys:revoke',            N'Revoke API Keys',          N'Revoke API keys',                                           N'apikeys:*', 2),
    (N'20000000-0000-0000-0000-000000000263', N'apikeys:rotate',            N'Rotate API Keys',          N'Rotate API keys',                                           N'apikeys:*', 2),
    (N'20000000-0000-0000-0000-000000000264', N'apikeys:validate',          N'Validate API Keys',        N'Call the API key validation endpoint',                      N'apikeys:*', 2),
    -- Webhook keys
    (N'20000000-0000-0000-0000-000000000270', N'webhookkeys:read',          N'View Webhook Keys',        N'View webhook keys and their metadata',                      N'webhookkeys:*', 2),
    (N'20000000-0000-0000-0000-000000000271', N'webhookkeys:create',        N'Create Webhook Keys',      N'Issue new webhook keys',                                    N'webhookkeys:*', 2),
    (N'20000000-0000-0000-0000-000000000272', N'webhookkeys:revoke',        N'Revoke Webhook Keys',      N'Revoke webhook keys',                                       N'webhookkeys:*', 2),
    (N'20000000-0000-0000-0000-000000000273', N'webhookkeys:rotate',        N'Rotate Webhook Keys',      N'Rotate webhook keys',                                       N'webhookkeys:*', 2),
    (N'20000000-0000-0000-0000-000000000274', N'webhookkeys:validate',      N'Validate Webhook Keys',    N'Call the webhook key validation endpoint',                  N'webhookkeys:*', 2),
    -- Secrets. A dot separator, so NO colon wildcard can ever cover it: it hangs
    -- straight off "*" and must always be granted explicitly. That is deliberate.
    (N'20000000-0000-0000-0000-000000000280', N'secrets.manage',            N'Manage Secrets',           N'Read and rotate the encrypted secret store',                N'*', 1),
    -- Organization permission management. Belongs to 07's org family, not to a
    -- platform area: org-admin already holds org:permissions:* and satisfies the
    -- gate by prefix, but the row has to exist to be grantable on its own.
    (N'20000000-0000-0000-0000-000000000290', N'org:permissions:manage',    N'Manage Org Permissions',   N'Grant and revoke member permissions within an organization', N'org:permissions:*', 3);

INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
SELECT l.[Id], l.[Code], l.[Name], l.[Description], NULL, p.[Id], l.[Level], 0, 1, GETUTCDATE(), @SystemUserId
FROM @Leaves l
INNER JOIN [dbo].[Permissions] p ON p.[Code] = l.[ParentCode]
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] e WHERE e.[Code] = l.[Code]);

PRINT 'Seeded platform leaf permissions (' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' new)';
GO

-- ============================================================
-- Role grants
-- ============================================================
-- Resolved by code, guarded by NOT EXISTS. UQ_RolePermissions spans
-- (RoleId, PermissionId), so a repeat publish is a no-op.
--
-- Withheld from admin, deliberately: secrets.manage and system-settings:manage
-- rewrite the JWT signing material, the mail transport and the rate limits, and
-- privacy-policy:manage publishes legally binding text. Those stay with
-- super-admin. That withholding is real rather than decorative only because the
-- no-amplification rule shipped first: without it, admin holds permissions:* and
-- users:manage-permissions and could simply grant itself the difference.
--
-- auditlogs:export is withheld from auditor for the same reason in miniature:
-- reading the log is the duty, bulk export is the exfiltration surface.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @Grants TABLE ([RoleCode] NVARCHAR(100), [PermissionCode] NVARCHAR(200));

INSERT INTO @Grants ([RoleCode], [PermissionCode]) VALUES
    (N'admin', N'users:*'),
    (N'admin', N'roles:*'),
    (N'admin', N'permissions:*'),
    (N'admin', N'applications:*'),
    (N'admin', N'auditlogs:*'),
    (N'admin', N'apikeys:*'),
    (N'admin', N'webhookkeys:*'),
    (N'admin', N'organizations:read'),
    (N'admin', N'organizations:manage'),
    (N'admin', N'platform-settings:manage'),
    (N'admin', N'notification-templates:*'),
    (N'admin', N'notification-layouts:manage'),
    (N'admin', N'privacy-policy:read'),

    -- "Can manage users but not roles or permissions" (its own seeded
    -- description). roles:read rides along because the role picker on a user is
    -- unusable without it.
    (N'user-manager', N'users:*'),
    (N'user-manager', N'roles:read'),

    -- "Read-only access to audit logs and reports". users:read so the actor
    -- column resolves to a name instead of an id.
    (N'auditor', N'auditlogs:read'),
    (N'auditor', N'users:read');

INSERT INTO [dbo].[RolePermissions] ([Id], [RoleId], [PermissionId], [GrantedAt], [GrantedBy])
SELECT NEWID(), r.[Id], p.[Id], GETUTCDATE(), @SystemUserId
FROM @Grants g
INNER JOIN [dbo].[Roles] r ON r.[Code] = g.[RoleCode] AND r.[ApplicationId] IS NULL
INNER JOIN [dbo].[Permissions] p ON p.[Code] = g.[PermissionCode]
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[RolePermissions] rp
    WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionId] = p.[Id]);

PRINT 'Granted platform permissions to built-in roles (' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' new)';
GO

-- ============================================================
-- Retire the codes no controller enforces
-- ============================================================
-- The rule this file exists to satisfy runs both ways: every enforced code has
-- a row, and every row is an enforced code. These are the other direction — 21
-- codes that are seeded, grantable through the console, and reach no gate in
-- the system. Granting one looks like it did something and does nothing, which
-- is worse than the permission being absent.
--
--   * the 15 auth:-prefixed codes, superseded by the short codes above;
--   * org:read, org:delete, org:permissions:grant, org:permissions:revoke,
--     which 07 seeds but no endpoint asks for (the org gates are org:update,
--     org:members:*, org:apps:* and org:permissions:read|manage);
--   * profile:read and profile:update, granted to every user and enforced
--     nowhere - dead in the opposite direction.
--
-- DEACTIVATED, NOT DELETED. Every effective-permission query filters
-- [IsActive] = 1, so an inactive row is ungrantable, invisible in the console
-- picker and contributes nothing to a token: the user-visible outcome is
-- identical to deletion. Deletion is not, because six tables reference
-- Permissions - RolePermissions, UserPermissions, OrganizationUserPermissions,
-- PermissionImplications (twice) and ApiKeyScopes - and a single surviving
-- reference would fail the delete, which under :on error exit aborts the whole
-- publish and silently skips every seed step after it.
--
-- Placed at the END of the publish rather than in an Upgrades script, and this
-- is load-bearing: upgrade scripts are included before Step 3, which recreates
-- the auth: rows from scratch on a fresh database. Running last makes a fresh
-- install and an upgraded one converge on the same state. Removing the Step 3
-- inserts outright is the tidier end state, but it first requires repointing
-- the ParentId that Step 8 and seeds 13/15/17 hang off auth:* - a separate
-- change with its own FK risk.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

UPDATE [dbo].[Permissions]
SET [IsActive] = 0,
    [ModifiedAt] = GETUTCDATE(),
    [ModifiedBy] = @SystemUserId
WHERE [IsActive] = 1
  AND ([Code] LIKE N'auth:%'
       OR [Code] IN (N'org:read', N'org:delete', N'org:permissions:grant',
                     N'org:permissions:revoke', N'profile:read', N'profile:update'));

PRINT 'Retired unenforced permission codes (' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' deactivated)';
GO
