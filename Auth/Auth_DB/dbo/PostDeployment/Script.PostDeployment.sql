/*
Post-Deployment Script for Auth_DB
This script runs after the database schema is deployed.
It creates all seed data in the correct order.
*/

PRINT 'Starting post-deployment seed data...';
PRINT '======================================';

-- ============================================
-- STEP 1: DEFAULT APPLICATIONS
-- ============================================
PRINT '';
PRINT 'Step 1: Creating default applications...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Insert Auth System application (if not exists)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [Id] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Applications]
    (
        [Id],
        [Code],
        [Name],
        [Description],
        [BaseUrl],
        [IsActive],
        [AllowSelfRegistration],
        [RequireTwoFactor],
        [SessionTimeoutMinutes],
        [MaxConcurrentSessions],
        [CreatedAt],
        [CreatedBy]
    )
    VALUES
    (
        @AuthAppId,
        N'auth',
        N'Auth System',
        N'Central Authentication and Authorization System',
        N'https://auth.company.com',
        1,
        0,  -- No self-registration for Auth System
        0,  -- 2FA optional
        60, -- 60 minute session timeout
        10, -- Max 10 concurrent sessions
        GETUTCDATE(),
        @SystemUserId
    );

    PRINT 'Created Auth System application';
END
ELSE
BEGIN
    PRINT 'Auth System application already exists';
END
GO

-- ============================================
-- STEP 2: DEFAULT ROLES
-- ============================================
PRINT '';
PRINT 'Step 2: Creating default roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Super Admin Role (global, has all permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'super-admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'super-admin', N'Super Administrator', N'Has all permissions across all applications', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Super Admin role';
END

-- System Admin Role (Auth System specific)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'admin' AND [ApplicationId] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000002', N'admin', N'Administrator', N'Can manage users, roles, and permissions in Auth System', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Admin role';
END

-- User Manager Role
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user-manager' AND [ApplicationId] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000003', N'user-manager', N'User Manager', N'Can manage users but not roles or permissions', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created User Manager role';
END

-- Auditor Role (read-only access to audit logs)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'auditor' AND [ApplicationId] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'auditor', N'Auditor', N'Read-only access to audit logs and reports', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);
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
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

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
    VALUES (N'20000000-0000-0000-0000-000000000002', N'auth:*', N'All Auth Permissions', N'Full access to Auth System', @AuthAppId, N'20000000-0000-0000-0000-000000000001', 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auth:* permission';
END

-- Level 2: Resource wildcards
-- Users
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000010', N'auth:users:*', N'All User Permissions', N'Full access to user management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Roles
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000020', N'auth:roles:*', N'All Role Permissions', N'Full access to role management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000030', N'auth:permissions:*', N'All Permission Permissions', N'Full access to permission management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Audit
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000040', N'auth:audit:*', N'All Audit Permissions', N'Full access to audit logs', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 3: Specific action permissions
-- User actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000011', N'auth:users:read', N'Read Users', N'View user information', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000012', N'auth:users:create', N'Create Users', N'Create new users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000013', N'auth:users:update', N'Update Users', N'Modify user information', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000014', N'auth:users:delete', N'Delete Users', N'Delete users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:manage-roles')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000015', N'auth:users:manage-roles', N'Manage User Roles', N'Assign and remove roles from users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Role actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000021', N'auth:roles:read', N'Read Roles', N'View role information', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000022', N'auth:roles:create', N'Create Roles', N'Create new roles', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000023', N'auth:roles:update', N'Update Roles', N'Modify role information', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000024', N'auth:roles:delete', N'Delete Roles', N'Delete roles', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Audit actions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000041', N'auth:audit:read', N'Read Audit Logs', N'View audit logs', @AuthAppId, N'20000000-0000-0000-0000-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);
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
        1,  -- Email confirmed
        2,  -- Status: Inactive (cannot login)
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created system user';
END

-- Create admin user
-- DEFAULT PASSWORD: Admin@123! (MUST be changed on first login!)
-- Hash generated with Argon2id: m=65536,t=3,p=4
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
        -- This is a placeholder hash - the actual hash must be generated by the application
        -- The application should update this on first run or provide a setup wizard
        N'$argon2id$v=19$m=65536,t=3,p=4$c2FsdHNhbHRzYWx0c2FsdA$PLACEHOLDER_HASH_UPDATE_ON_FIRST_RUN',
        N'System',
        N'Administrator',
        N'en',
        N'UTC',
        1,  -- Email confirmed
        1,  -- Status: Active
        1,  -- Must change password on first login
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created admin user (password must be set via application)';
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
