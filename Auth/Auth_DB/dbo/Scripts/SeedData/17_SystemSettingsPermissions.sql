-- System Settings Permissions Seed Data
-- Direct child of auth:* (like platform-settings:manage) so the seeded admin
-- role inherits it; super-admin is covered by the global * wildcard.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- system-settings:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'system-settings:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000D0', N'system-settings:manage', N'Manage System Settings', N'View and change platform configuration (password policy, email, JWT, rate limits, ...) from the console', NULL, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created system-settings:manage permission';
END
GO
