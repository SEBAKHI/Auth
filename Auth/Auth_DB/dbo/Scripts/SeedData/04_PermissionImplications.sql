-- Permission Implications Seed Data
-- Defines which permissions imply other permissions

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
