-- Default Roles Seed Data
-- Creates system-level roles for the Auth System

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
