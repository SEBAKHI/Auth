-- Role Permissions Seed Data
-- Assigns permissions to default roles

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
