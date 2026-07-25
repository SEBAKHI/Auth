-- Notification Management Permissions Seed Data
-- Wildcard parent + children under auth:* so the seeded admin role inherits them;
-- super-admin is covered by the global * wildcard.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Parent: notification-templates:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'notification-templates:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B0', N'notification-templates:*', N'All Notification Template Permissions', N'Full access to notification template management', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created notification-templates:* permission';
END

-- notification-templates:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'notification-templates:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B1', N'notification-templates:read', N'Read Notification Templates', N'View notification templates, versions, translations, and types', NULL, N'20000000-0000-0000-0000-0000000000B0', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created notification-templates:read permission';
END

-- notification-templates:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'notification-templates:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B2', N'notification-templates:manage', N'Manage Notification Templates', N'Create, edit, preview, test-send, and delete notification templates and drafts', NULL, N'20000000-0000-0000-0000-0000000000B0', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created notification-templates:manage permission';
END

-- notification-templates:publish
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'notification-templates:publish')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B3', N'notification-templates:publish', N'Publish Notification Templates', N'Publish, unpublish, and roll back notification template versions', NULL, N'20000000-0000-0000-0000-0000000000B0', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created notification-templates:publish permission';
END

-- notification-layouts:manage (direct child of auth:*, like platform-settings:manage)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'notification-layouts:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B4', N'notification-layouts:manage', N'Manage Notification Layouts', N'Edit, preview, and publish notification layouts (shared visual identity)', NULL, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created notification-layouts:manage permission';
END
GO

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Implication: notification-templates:manage -> notification-templates:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-0000000000B2' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-0000000000B1')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B2', N'20000000-0000-0000-0000-0000000000B1', GETUTCDATE(), @SystemUserId);
    PRINT 'Created implication: notification-templates:manage -> read';
END

-- Implication: notification-templates:publish -> notification-templates:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-0000000000B3' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-0000000000B1')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000B3', N'20000000-0000-0000-0000-0000000000B1', GETUTCDATE(), @SystemUserId);
    PRINT 'Created implication: notification-templates:publish -> read';
END
GO
