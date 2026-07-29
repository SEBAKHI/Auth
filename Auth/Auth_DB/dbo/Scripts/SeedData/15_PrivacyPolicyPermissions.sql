-- Privacy Policy Permissions Seed Data
-- Publishing legal text is its own duty, separate from notification management:
-- the policy is a legally binding disclosure, and who may reword it is not
-- necessarily who may operate email templates.
-- Wildcard parent + children under auth:* so the seeded admin role inherits them;
-- super-admin is covered by the global * wildcard.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Parent: privacy-policy:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'privacy-policy:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000C0', N'privacy-policy:*', N'All Privacy Policy Permissions', N'Full access to privacy policy versions and content', NULL, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created privacy-policy:* permission';
END

-- privacy-policy:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'privacy-policy:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000C1', N'privacy-policy:read', N'Read Privacy Policy', N'View privacy policy versions and their language documents', NULL, N'20000000-0000-0000-0000-0000000000C0', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created privacy-policy:read permission';
END

-- privacy-policy:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'privacy-policy:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000C2', N'privacy-policy:manage', N'Manage Privacy Policy', N'Record versions, edit policy content, publish, and notify users of changes', NULL, N'20000000-0000-0000-0000-0000000000C0', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created privacy-policy:manage permission';
END
GO

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Implication: privacy-policy:manage -> privacy-policy:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-0000000000C2' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-0000000000C1')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000C2', N'20000000-0000-0000-0000-0000000000C1', GETUTCDATE(), @SystemUserId);
    PRINT 'Created implication: privacy-policy:manage -> read';
END
GO
