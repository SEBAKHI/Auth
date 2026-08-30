/*
Post-Deployment Script for Auth_DB
This script runs after the database schema is deployed.
It creates all seed data in the correct order.
*/

-- Reconcile historical rows before seed/assignment logic runs. The included
-- scripts are idempotent and are intentionally part of every database publish.
-- The retire script MUST stay before the seed steps: it re-scopes existing
-- app-scoped RBAC rows so the Code + ApplicationId IS NULL guards below match
-- them instead of re-inserting hardcoded primary keys.
:r ..\Scripts\Upgrades\2026-07-20_PurgeInactiveUserAssignments.sql
:r ..\Scripts\Upgrades\2026-07-26_RetirePlatformApplication.sql
:r ..\Scripts\Upgrades\2026-07-31_EmailLayoutLogoPlatformDriven.sql
-- Supersedes the logo block the 2026-07-31 script installs: that one pointed the layout at
-- the raw uploaded logo (alpha WebP), which Gmail flattens onto black. Must run after it.
:r ..\Scripts\Upgrades\2026-08-10_EmailLayoutDarkModeAndLogo.sql
-- Consumes the layout the previous script installs (its fingerprint is that generation's
-- <body> tag), so this ordering is load-bearing, not cosmetic.
:r ..\Scripts\Upgrades\2026-08-10_EmailLayoutRtlHardening.sql
-- Colour-only and targeted at four declarations, so it consumes no fingerprint of its own.
-- It must still run AFTER both 2026-08-10 scripts: each of those overwrites the whole layout
-- column with a frozen literal carrying the old #FAFAF9/#17171A footer, so moving this
-- include above either one applies the fix and then discards it in the same deploy - and the
-- log still reads as a success.
:r ..\Scripts\Upgrades\2026-08-23_EmailLayoutFooterSurface.sql
-- Independent of the e-mail layout chain above; ordering against it does not matter.
-- Cancels pending invitations whose token predates hashing, so they fail with a
-- stated reason instead of a silent "not found" the invitee cannot interpret.
:r ..\Scripts\Upgrades\2026-08-30_InvitationTokenHashing.sql

PRINT 'Starting post-deployment seed data...';
PRINT '======================================';

-- (Retired 2026-07-26) The platform "auth" Application row is no longer seeded;
-- Applications holds external client applications only. Platform RBAC is global
-- (ApplicationId = NULL). See Upgrades\2026-07-26_RetirePlatformApplication.sql.

-- ============================================
-- STEP 2: DEFAULT ROLES
-- ============================================
PRINT '';
PRINT 'Step 2: Creating default roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Super Admin Role (global, has all permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'super-admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'super-admin', N'Super Administrator', N'Has all permissions across all applications', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Super Admin role';
END

-- Platform Admin Role (global)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000002', N'admin', N'Administrator', N'Can manage users, roles, and permissions across the platform', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Admin role';
END

-- User Manager Role (global)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user-manager' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000003', N'user-manager', N'User Manager', N'Can manage users but not roles or permissions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created User Manager role';
END

-- Auditor Role (global, read-only access to audit logs)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'auditor' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'auditor', N'Auditor', N'Read-only access to audit logs and reports', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Auditor role';
END

-- Basic User Role (global, minimal permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000005', N'user', N'User', N'Basic authenticated user with profile access', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created User role';
END
GO

-- ============================================
-- STEP 3: DEFAULT PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 3: Creating default permissions...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Level 0: Global wildcard (Super Admin only)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000001', N'*', N'All Permissions', N'Super admin - grants all permissions', NULL, NULL, 0, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created * permission';
END

-- Level 1: Auth System wildcard
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000002', N'auth:*', N'All Auth Permissions', N'Full access to Auth System', NULL, N'20000000-0000-0000-0000-000000000001', 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auth:* permission';
END

-- Level 2: Resource wildcards
-- Users
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000010', N'auth:users:*', N'All User Permissions', N'Full access to user management', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Roles
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000020', N'auth:roles:*', N'All Role Permissions', N'Full access to role management', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000030', N'auth:permissions:*', N'All Permission Permissions', N'Full access to permission management', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Audit
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000040', N'auth:audit:*', N'All Audit Permissions', N'Full access to audit logs', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 3: Specific action permissions
-- User actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000011', N'auth:users:read', N'Read Users', N'View user information', NULL, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000012', N'auth:users:create', N'Create Users', N'Create new users', NULL, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000013', N'auth:users:update', N'Update Users', N'Modify user information', NULL, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000014', N'auth:users:delete', N'Delete Users', N'Delete users', NULL, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:manage-roles')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000015', N'auth:users:manage-roles', N'Manage User Roles', N'Assign and remove roles from users', NULL, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Role actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000021', N'auth:roles:read', N'Read Roles', N'View role information', NULL, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000022', N'auth:roles:create', N'Create Roles', N'Create new roles', NULL, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000023', N'auth:roles:update', N'Update Roles', N'Modify role information', NULL, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000024', N'auth:roles:delete', N'Delete Roles', N'Delete roles', NULL, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Audit actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000041', N'auth:audit:read', N'Read Audit Logs', N'View audit logs', NULL, N'20000000-0000-0000-0000-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Profile permissions (global, for all authenticated users)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'profile:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000100', N'profile:read', N'Read Own Profile', N'View own profile information', NULL, NULL, 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'profile:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000101', N'profile:update', N'Update Own Profile', N'Modify own profile information', NULL, NULL, 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

PRINT 'Created default permissions';
GO

-- ============================================
-- STEP 4: PERMISSION IMPLICATIONS
-- ============================================
PRINT '';
PRINT 'Step 4: Creating permission implications...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Write implies Read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000013' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000013', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);
    -- auth:users:update implies auth:users:read
END

-- Delete implies Read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000014' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000014', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);
    -- auth:users:delete implies auth:users:read
END

-- Create implies Read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000012' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000012', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);
    -- auth:users:create implies auth:users:read
END

-- Manage-roles implies Read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000015' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000015', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);
    -- auth:users:manage-roles implies auth:users:read
END

-- Manage-roles also implies roles:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000015' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000015', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);
    -- auth:users:manage-roles implies auth:roles:read
END

-- Role write permissions imply read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000022' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000022', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);
    -- auth:roles:create implies auth:roles:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000023' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000023', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);
    -- auth:roles:update implies auth:roles:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000024' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000024', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);
    -- auth:roles:delete implies auth:roles:read
END

-- Profile update implies profile read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000101' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000100')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000101', N'20000000-0000-0000-0000-000000000100', GETUTCDATE(), @SystemUserId);
    -- profile:update implies profile:read
END

PRINT 'Created permission implications';
GO

-- ============================================
-- STEP 5: ROLE PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 5: Assigning permissions to roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Super Admin gets the global wildcard (*)
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000001' AND [PermissionId] = N'20000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'20000000-0000-0000-0000-000000000001', GETUTCDATE(), @SystemUserId);
    -- super-admin gets *
END

-- Admin gets auth:* (all Auth System permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000002' AND [PermissionId] = N'20000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000002', N'20000000-0000-0000-0000-000000000002', GETUTCDATE(), @SystemUserId);
    -- admin gets auth:*
END

-- User Manager gets auth:users:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000003' AND [PermissionId] = N'20000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000003', N'20000000-0000-0000-0000-000000000010', GETUTCDATE(), @SystemUserId);
    -- user-manager gets auth:users:*
END

-- Auditor gets auth:audit:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000004' AND [PermissionId] = N'20000000-0000-0000-0000-000000000041')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'20000000-0000-0000-0000-000000000041', GETUTCDATE(), @SystemUserId);
    -- auditor gets auth:audit:read
END

-- Auditor also gets auth:users:read (to see who did what)
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000004' AND [PermissionId] = N'20000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);
    -- auditor gets auth:users:read
END

-- Basic User gets profile:read and profile:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000005' AND [PermissionId] = N'20000000-0000-0000-0000-000000000100')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000005', N'20000000-0000-0000-0000-000000000100', GETUTCDATE(), @SystemUserId);
    -- user gets profile:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000005' AND [PermissionId] = N'20000000-0000-0000-0000-000000000101')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000005', N'20000000-0000-0000-0000-000000000101', GETUTCDATE(), @SystemUserId);
    -- user gets profile:update
END

PRINT 'Created role permissions';
GO

-- ============================================
-- STEP 5.5: ORGANIZATION ROLES AND PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 5.5: Creating organization roles and permissions...';

:r ..\Scripts\SeedData\07_OrganizationRolesPermissions.sql
GO

-- ============================================
-- STEP 6: ADMIN USER
-- ============================================
PRINT '';
PRINT 'Step 6: Creating admin user...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AdminUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000002';
DECLARE @SuperAdminRoleId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000001';
DECLARE @UserRoleId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000005';

-- Create system user (used for seeding and system operations)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = @SystemUserId)
BEGIN
    INSERT INTO [dbo].[Users]
    (
        [Id],
        [Username],
        [Email],
        [NormalizedEmail],
        [PasswordHash],
        [FirstName],
        [LastName],
        [PreferredLanguage],
        [TimeZone],
        [Theme],
        [IsEmailConfirmed],
        [Status],
        [CreatedAt],
        [CreatedBy]
    )
    VALUES
    (
        @SystemUserId,
        N'system',
        N'system@localhost',
        N'SYSTEM@LOCALHOST',
        N'$argon2id$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',  -- Placeholder - cannot login
        N'System',
        N'Account',
        N'en',
        N'UTC',
        N'system',
        1,  -- Email confirmed
        2,  -- Status: Inactive (cannot login)
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created system user';
END

-- Create admin user
-- NO PASSWORD IS SEEDED. PasswordHash is left NULL on purpose: a hash committed here is a
-- published credential for every deployment of this system, and LoginCommandHandler rejects a
-- null hash before it reaches the verifier, so nobody can sign in as this account until an
-- operator sets a password out of band.
-- BOOTSTRAP: run the Auth_Setup console app, give it the password you have chosen, and execute
-- the UPDATE statement it prints. Until then this account exists, holds super-admin, and cannot
-- authenticate.
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = @AdminUserId)
BEGIN
    INSERT INTO [dbo].[Users]
    (
        [Id],
        [Username],
        [Email],
        [NormalizedEmail],
        [PasswordHash],
        [FirstName],
        [LastName],
        [PreferredLanguage],
        [TimeZone],
        [Theme],
        [IsEmailConfirmed],
        [Status],
        [MustChangePassword],
        [CreatedAt],
        [CreatedBy]
    )
    VALUES
    (
        @AdminUserId,
        N'admin',
        N'admin@company.com',
        N'ADMIN@COMPANY.COM',
        -- No password. See the BOOTSTRAP note above. MustChangePassword stays 1 so the console still
        -- prompts, but the real gate is the null hash, which the server enforces rather than the client.
        NULL,
        N'System',
        N'Administrator',
        N'en',
        N'UTC',
        N'system',
        1,  -- Email confirmed
        1,  -- Status: Active
        1,  -- Must change password on first login
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created admin user with NO password - run Auth_Setup and apply the UPDATE it prints';
END

-- Assign Super Admin role to admin user
IF NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = @AdminUserId AND [RoleId] = @SuperAdminRoleId)
BEGIN
    INSERT INTO [dbo].[UserRoles]
    (
        [UserId],
        [RoleId],
        [ApplicationId],
        [AssignedAt],
        [AssignedBy],
        [IsActive]
    )
    VALUES
    (
        @AdminUserId,
        @SuperAdminRoleId,
        NULL,  -- Global role (all applications)
        GETUTCDATE(),
        @SystemUserId,
        1
    );
    PRINT 'Assigned Super Admin role to admin user';
END

-- Also assign User role (for profile access)
IF NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = @AdminUserId AND [RoleId] = @UserRoleId)
BEGIN
    INSERT INTO [dbo].[UserRoles]
    (
        [UserId],
        [RoleId],
        [ApplicationId],
        [AssignedAt],
        [AssignedBy],
        [IsActive]
    )
    VALUES
    (
        @AdminUserId,
        @UserRoleId,
        NULL,  -- Global role
        GETUTCDATE(),
        @SystemUserId,
        1
    );
    PRINT 'Assigned User role to admin user';
END

PRINT 'Admin user setup complete';
GO

-- ============================================
-- STEP 7: EXTERNAL AUTH PROVIDERS
-- ============================================
PRINT '';
PRINT 'Step 7: Creating external auth providers...';

:r ..\Scripts\SeedData\09_ExternalAuthProviders.sql
GO

-- ============================================
-- STEP 8: PLATFORM SETTINGS
-- ============================================
PRINT '';
PRINT 'Step 8: Creating platform settings...';

-- Singleton branding row (name/logo shown across the console and auth screens)
IF NOT EXISTS (SELECT 1 FROM [dbo].[PlatformSettings] WHERE [Id] = '30000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[PlatformSettings] ([Id], [PlatformName])
    VALUES ('30000000-0000-0000-0000-000000000001', N'Auth Console');
    PRINT 'Created default platform settings';
END
ELSE
BEGIN
    PRINT 'Platform settings already exist';
END

-- platform-settings:manage permission (child of auth:* so the seeded admin
-- role inherits it; super-admin is covered by the global * wildcard)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'platform-settings:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A2', N'platform-settings:manage', N'Manage Platform Settings', N'Manage platform branding (name and logo)', NULL, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created platform-settings:manage permission';
END
GO

-- Platform-wide organizations administration (children of auth:* so the
-- seeded admin role inherits them; distinct from the membership-scoped org:*)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'organizations:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A3', N'organizations:read', N'Read All Organizations', N'View any organization on the platform, including ones the caller is not a member of', NULL, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created organizations:read permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'organizations:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A4', N'organizations:manage', N'Manage All Organizations', N'Administer any organization on the platform, including delete', NULL, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created organizations:manage permission';
END
GO

-- ============================================
-- STEP 9: NOTIFICATION TYPES
-- ============================================
PRINT '';
PRINT 'Step 9: Creating notification types...';

:r ..\Scripts\SeedData\10_NotificationTypes.sql
GO

-- ============================================
-- STEP 10: NOTIFICATION LAYOUTS
-- ============================================
PRINT '';
PRINT 'Step 10: Creating notification layouts...';

:r ..\Scripts\SeedData\11_NotificationLayouts.sql
GO

-- ============================================
-- STEP 11: NOTIFICATION TEMPLATES
-- ============================================
PRINT '';
PRINT 'Step 11: Creating notification templates...';

:r ..\Scripts\SeedData\12_NotificationTemplates.sql
GO

-- ============================================
-- STEP 12: NOTIFICATION PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 12: Creating notification permissions...';

:r ..\Scripts\SeedData\13_NotificationPermissions.sql
:r ..\Scripts\SeedData\14_PrivacyPolicyVersions.sql
:r ..\Scripts\SeedData\15_PrivacyPolicyPermissions.sql
:r ..\Scripts\SeedData\16_PrivacyPolicyContent.sql
GO

-- ============================================
-- STEP 13: SYSTEM SETTINGS PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 13: Creating system settings permissions...';

:r ..\Scripts\SeedData\17_SystemSettingsPermissions.sql
GO

-- ============================================
-- STEP 14: PLATFORM PERMISSIONS
-- ============================================
-- Runs LAST on purpose. It seeds every code the API enforces, grants them to the
-- built-in roles, and then retires the codes no controller asks for - including
-- the auth:-prefixed rows Step 3 creates a few hundred lines above. Ordering it
-- after Step 3 is what lets a fresh database and an upgraded one end up with the
-- same permission set.
PRINT '';
PRINT 'Step 14: Creating platform permissions and role grants...';

:r ..\Scripts\SeedData\18_PlatformPermissions.sql
GO

-- ============================================
-- COMPLETION
-- ============================================
PRINT '';
PRINT '======================================';
PRINT 'Post-deployment seed data complete!';
PRINT '';
PRINT 'IMPORTANT NOTES:';
PRINT '1. Update the admin user password hash before production deployment';
PRINT '2. Review all seed data for your environment';
PRINT '3. Consider adding additional application-specific roles and permissions';
GO
