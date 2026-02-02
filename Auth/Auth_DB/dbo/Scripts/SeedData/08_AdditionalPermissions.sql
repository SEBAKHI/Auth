-- Additional Permissions Seed Data
-- Creates permissions for new API endpoints (v2 expansion)

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- ============================================================
-- User Management Additional Permissions
-- ============================================================

-- Parent: users:* (20000000-0000-0000-0000-000000000010)

-- users:manage - For lock/unlock/activate/deactivate
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000016', N'users:manage', N'Manage Users', N'Lock, unlock, activate, and deactivate user accounts', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:manage permission';
END

-- users:manage-permissions - For granting/revoking direct permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:manage-permissions')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000017', N'users:manage-permissions', N'Manage User Permissions', N'Grant and revoke direct permissions for users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:manage-permissions permission';
END

-- ============================================================
-- Permission Management Additional Permissions
-- ============================================================

-- Parent: permissions:* (create parent if not exists)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000050', N'permissions:*', N'All Permission Permissions', N'Full access to permission management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:* permission';
END

-- permissions:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000051', N'permissions:read', N'Read Permissions', N'View permission information', @AuthAppId, N'20000000-0000-0000-0000-000000000050', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:read permission';
END

-- permissions:create
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000052', N'permissions:create', N'Create Permissions', N'Create new permissions', @AuthAppId, N'20000000-0000-0000-0000-000000000050', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:create permission';
END

-- permissions:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000053', N'permissions:update', N'Update Permissions', N'Modify permission information', @AuthAppId, N'20000000-0000-0000-0000-000000000050', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:update permission';
END

-- permissions:delete
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000054', N'permissions:delete', N'Delete Permissions', N'Delete permissions', @AuthAppId, N'20000000-0000-0000-0000-000000000050', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:delete permission';
END

-- permissions:manage - For managing permission implications
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'permissions:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000055', N'permissions:manage', N'Manage Permission Implications', N'Add and remove permission implications', @AuthAppId, N'20000000-0000-0000-0000-000000000050', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created permissions:manage permission';
END

-- ============================================================
-- Application Management Permissions
-- ============================================================

-- Parent: applications:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000060', N'applications:*', N'All Application Permissions', N'Full access to application management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:* permission';
END

-- applications:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000061', N'applications:read', N'Read Applications', N'View application information', @AuthAppId, N'20000000-0000-0000-0000-000000000060', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:read permission';
END

-- applications:create
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000062', N'applications:create', N'Create Applications', N'Create new applications', @AuthAppId, N'20000000-0000-0000-0000-000000000060', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:create permission';
END

-- applications:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000063', N'applications:update', N'Update Applications', N'Modify application information', @AuthAppId, N'20000000-0000-0000-0000-000000000060', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:update permission';
END

-- applications:delete
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'applications:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000064', N'applications:delete', N'Delete Applications', N'Delete applications', @AuthAppId, N'20000000-0000-0000-0000-000000000060', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created applications:delete permission';
END

-- ============================================================
-- Audit Log Management Permissions
-- ============================================================

-- Parent: auditlogs:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auditlogs:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000070', N'auditlogs:*', N'All Audit Log Permissions', N'Full access to audit log management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auditlogs:* permission';
END

-- auditlogs:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auditlogs:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000071', N'auditlogs:read', N'Read Audit Logs', N'View audit log entries', @AuthAppId, N'20000000-0000-0000-0000-000000000070', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auditlogs:read permission';
END

-- auditlogs:export
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auditlogs:export')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000072', N'auditlogs:export', N'Export Audit Logs', N'Export audit logs to CSV or JSON', @AuthAppId, N'20000000-0000-0000-0000-000000000070', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created auditlogs:export permission';
END

-- ============================================================
-- Organization Management Additional Permissions
-- ============================================================

-- Parent: org:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000080', N'org:*', N'All Organization Permissions', N'Full access to organization management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:* permission';
END

-- org:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000081', N'org:update', N'Update Organization', N'Modify organization information', @AuthAppId, N'20000000-0000-0000-0000-000000000080', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:update permission';
END

-- org:members:* (sub-wildcard)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000082', N'org:members:*', N'All Organization Member Permissions', N'Full access to organization member management', @AuthAppId, N'20000000-0000-0000-0000-000000000080', 3, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:members:* permission';
END

-- org:members:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000083', N'org:members:read', N'Read Organization Members', N'View organization members', @AuthAppId, N'20000000-0000-0000-0000-000000000082', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:members:read permission';
END

-- org:members:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000084', N'org:members:manage', N'Manage Organization Members', N'Add, update, and remove organization members', @AuthAppId, N'20000000-0000-0000-0000-000000000082', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:members:manage permission';
END

-- org:members:invite
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:invite')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000085', N'org:members:invite', N'Invite Organization Members', N'Send invitations to join organization', @AuthAppId, N'20000000-0000-0000-0000-000000000082', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:members:invite permission';
END

-- org:apps:* (sub-wildcard)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000086', N'org:apps:*', N'All Organization App Permissions', N'Full access to organization application management', @AuthAppId, N'20000000-0000-0000-0000-000000000080', 3, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:apps:* permission';
END

-- org:apps:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000087', N'org:apps:read', N'Read Organization Applications', N'View applications enabled for organization', @AuthAppId, N'20000000-0000-0000-0000-000000000086', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:apps:read permission';
END

-- org:apps:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000088', N'org:apps:manage', N'Manage Organization Applications', N'Enable, disable, and configure applications for organization', @AuthAppId, N'20000000-0000-0000-0000-000000000086', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:apps:manage permission';
END

-- org:permissions:* (sub-wildcard)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000089', N'org:permissions:*', N'All Organization Permission Management', N'Full access to organization permission management', @AuthAppId, N'20000000-0000-0000-0000-000000000080', 3, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:permissions:* permission';
END

-- org:permissions:manage
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000090', N'org:permissions:manage', N'Manage Organization User Permissions', N'Assign roles and grant permissions to organization members', @AuthAppId, N'20000000-0000-0000-0000-000000000089', 4, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:permissions:manage permission';
END

-- ============================================================
-- API Key Management Permissions
-- ============================================================

-- Parent: apikeys:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000091', N'apikeys:*', N'All API Key Permissions', N'Full access to API key management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:* permission';
END

-- apikeys:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000092', N'apikeys:read', N'Read API Keys', N'View API keys', @AuthAppId, N'20000000-0000-0000-0000-000000000091', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:read permission';
END

-- apikeys:create
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000093', N'apikeys:create', N'Create API Keys', N'Create new API keys', @AuthAppId, N'20000000-0000-0000-0000-000000000091', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:create permission';
END

-- apikeys:revoke
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:revoke')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000094', N'apikeys:revoke', N'Revoke API Keys', N'Revoke API keys', @AuthAppId, N'20000000-0000-0000-0000-000000000091', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:revoke permission';
END

-- apikeys:rotate
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'apikeys:rotate')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000095', N'apikeys:rotate', N'Rotate API Keys', N'Rotate API keys', @AuthAppId, N'20000000-0000-0000-0000-000000000091', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created apikeys:rotate permission';
END

-- ============================================================
-- Role Management Permissions (short codes)
-- ============================================================

-- Parent: roles:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000096', N'roles:*', N'All Role Permissions (Short)', N'Full access to role management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:* permission';
END

-- roles:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000097', N'roles:read', N'Read Roles', N'View role information', @AuthAppId, N'20000000-0000-0000-0000-000000000096', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:read permission';
END

-- roles:create
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000098', N'roles:create', N'Create Roles', N'Create new roles', @AuthAppId, N'20000000-0000-0000-0000-000000000096', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:create permission';
END

-- roles:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000099', N'roles:update', N'Update Roles', N'Modify role information', @AuthAppId, N'20000000-0000-0000-0000-000000000096', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:update permission';
END

-- roles:delete
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'roles:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009A', N'roles:delete', N'Delete Roles', N'Delete roles', @AuthAppId, N'20000000-0000-0000-0000-000000000096', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created roles:delete permission';
END

-- ============================================================
-- User Management Permissions (short codes matching controllers)
-- ============================================================

-- Parent: users:*
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009B', N'users:*', N'All User Permissions (Short)', N'Full access to user management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:* permission';
END

-- users:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009C', N'users:read', N'Read Users', N'View user information', @AuthAppId, N'20000000-0000-0000-0000-00000000009B', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:read permission';
END

-- users:create
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:create')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009D', N'users:create', N'Create Users', N'Create new users', @AuthAppId, N'20000000-0000-0000-0000-00000000009B', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:create permission';
END

-- users:update
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009E', N'users:update', N'Update Users', N'Modify user information', @AuthAppId, N'20000000-0000-0000-0000-00000000009B', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:update permission';
END

-- users:delete
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-00000000009F', N'users:delete', N'Delete Users', N'Delete users', @AuthAppId, N'20000000-0000-0000-0000-00000000009B', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:delete permission';
END

-- users:manage-roles
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:manage-roles')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A0', N'users:manage-roles', N'Manage User Roles', N'Assign and remove roles from users', @AuthAppId, N'20000000-0000-0000-0000-00000000009B', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created users:manage-roles permission';
END

-- ============================================================
-- Secrets Management Permissions
-- ============================================================

-- secrets.manage (note: uses . instead of :)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'secrets.manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A1', N'secrets.manage', N'Manage Secrets', N'Manage application secrets and configuration', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 3, 0, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created secrets.manage permission';
END

PRINT 'Created all additional permissions';
GO
