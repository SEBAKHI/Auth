-- ============================================
-- ORGANIZATION ROLES AND PERMISSIONS SEED DATA
-- ============================================

PRINT '';
PRINT 'Creating organization roles and permissions...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- ============================================
-- ORGANIZATION-LEVEL ROLES (Global roles, not app-specific)
-- These roles define what users can do within their organization
-- ============================================

-- Org Owner Role (full control over organization)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-owner' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000001', N'org-owner', N'Organization Owner', N'Full control over organization - cannot be removed', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org-owner role';
END

-- Org Admin Role (can manage members and app subscriptions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'org-admin', N'Organization Admin', N'Can manage members and app subscriptions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org-admin role';
END

-- Org Member Role (basic membership, access apps based on granted permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-member' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000003', N'org-member', N'Organization Member', N'Basic organization membership - access apps based on granted permissions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org-member role';
END

-- ============================================
-- ORGANIZATION MANAGEMENT PERMISSIONS
-- These permissions control organization-level operations
-- ============================================

-- Level 1: Organization wildcard
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000001', N'org:*', N'All Organization Permissions', N'Full organization management access', NULL, N'20000000-0000-0000-0000-000000000001', 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created org:* permission';
END

-- Level 3: Organization CRUD permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000011', N'org:read', N'View Organization', N'View organization details and settings', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:update')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000012', N'org:update', N'Update Organization', N'Modify organization settings', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:delete')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000013', N'org:delete', N'Delete Organization', N'Delete the organization', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 2: Members wildcard
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000020', N'org:members:*', N'All Member Permissions', N'Full member management access', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 3: Member management permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000021', N'org:members:read', N'View Members', N'View organization members', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:invite')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000022', N'org:members:invite', N'Invite Members', N'Invite new members to organization', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000023', N'org:members:manage', N'Manage Members', N'Update roles and remove members', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 2: Apps subscription wildcard
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000030', N'org:apps:*', N'All App Subscription Permissions', N'Full app subscription management', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 3: App subscription permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000031', N'org:apps:read', N'View Enabled Apps', N'View which apps are enabled for organization', NULL, N'20000000-0000-0000-0001-000000000030', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000032', N'org:apps:manage', N'Manage App Subscriptions', N'Enable or disable apps for organization', NULL, N'20000000-0000-0000-0001-000000000030', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 2: User permissions management wildcard
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000040', N'org:permissions:*', N'All User Permission Management', N'Full user permission management within organization', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);
END

-- Level 3: User permission management permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000041', N'org:permissions:read', N'View User Permissions', N'View member permissions within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:grant')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000042', N'org:permissions:grant', N'Grant User Permissions', N'Grant permissions to members within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:revoke')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000043', N'org:permissions:revoke', N'Revoke User Permissions', N'Revoke permissions from members within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);
END

PRINT 'Created organization permissions';

-- ============================================
-- PERMISSION IMPLICATIONS FOR ORGANIZATION PERMISSIONS
-- ============================================

-- org:update implies org:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000012' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000012', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);
END

-- org:delete implies org:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000013' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000011')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000013', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);
END

-- org:members:invite implies org:members:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000022' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000022', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);
END

-- org:members:manage implies org:members:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000023' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000021')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000023', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);
END

-- org:apps:manage implies org:apps:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000032' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000031')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000032', N'20000000-0000-0000-0001-000000000031', GETUTCDATE(), @SystemUserId);
END

-- org:permissions:grant implies org:permissions:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000042' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000041')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000042', N'20000000-0000-0000-0001-000000000041', GETUTCDATE(), @SystemUserId);
END

-- org:permissions:revoke implies org:permissions:read
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000043' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000041')
BEGIN
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000043', N'20000000-0000-0000-0001-000000000041', GETUTCDATE(), @SystemUserId);
END

PRINT 'Created organization permission implications';

-- ============================================
-- ROLE PERMISSIONS FOR ORGANIZATION ROLES
-- ============================================

-- Org Owner gets org:* (all organization permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000001' AND [PermissionId] = N'20000000-0000-0000-0001-000000000001')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000001', N'20000000-0000-0000-0001-000000000001', GETUTCDATE(), @SystemUserId);
    -- org-owner gets org:*
END

-- Org Admin gets member and app management
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000011')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);
    -- org-admin gets org:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000020')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000020', GETUTCDATE(), @SystemUserId);
    -- org-admin gets org:members:*
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000030')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000030', GETUTCDATE(), @SystemUserId);
    -- org-admin gets org:apps:*
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000040')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000040', GETUTCDATE(), @SystemUserId);
    -- org-admin gets org:permissions:*
END

-- Org Member gets org:read and org:members:read (basic visibility)
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000011')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);
    -- org-member gets org:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000021')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);
    -- org-member gets org:members:read
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000031')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000031', GETUTCDATE(), @SystemUserId);
    -- org-member gets org:apps:read
END

PRINT 'Created organization role permissions';
PRINT 'Organization roles and permissions seed data complete';
GO
