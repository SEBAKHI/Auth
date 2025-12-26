-- Default Permissions Seed Data
-- Creates hierarchical permissions for the Auth System

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
