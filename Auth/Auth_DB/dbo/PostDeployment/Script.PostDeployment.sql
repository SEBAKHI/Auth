/*
Post-Deployment Script for Auth_DB
This script runs after the database schema is deployed, on every publish.
It seeds the current reference data in order. Every step is an insert guarded
by IF NOT EXISTS, so a repeat publish adds what is missing and overwrites
nothing. See ..\..\README.md for how data changes reach an existing database.
*/

PRINT 'Starting post-deployment seed data...';
PRINT '======================================';

-- No Application row is seeded: Applications holds external client
-- applications only, and platform RBAC is global (ApplicationId = NULL).

-- ============================================
-- STEP 1: DEFAULT ROLES
-- ============================================
PRINT '';
PRINT 'Step 1: Creating default roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Super Admin Role (global, has all permissions)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'super-admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'super-admin', N'Super Administrator', N'Has all permissions across all applications', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Super Admin role';
END

-- Platform Admin Role (global)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'admin' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000002', N'admin', N'Administrator', N'Can manage users, roles, and permissions across the platform', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Admin role';
END

-- User Manager Role (global)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user-manager' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000003', N'user-manager', N'User Manager', N'Can manage users but not roles or permissions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created User Manager role';
END

-- Auditor Role (global, read-only access to audit logs)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'auditor' AND [ApplicationId] IS NULL)
BEGIN
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'auditor', N'Auditor', N'Read-only access to audit logs and reports', NULL, 1, 1, GETUTCDATE(), @SystemUserId);
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
-- STEP 2: GLOBAL WILDCARD
-- ============================================
PRINT '';
PRINT 'Step 2: Creating the global wildcard...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Level 0: Global wildcard (super-admin only). Every other platform code is
-- seeded by the area that owns it: organizations in 07, notifications in 13,
-- the privacy policy in 15, system settings in 17, the rest in 18.
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'*')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000001', N'*', N'All Permissions', N'Super admin - grants all permissions', NULL, NULL, 0, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created * permission';
END
GO

-- ============================================
-- STEP 3: SUPER-ADMIN GRANT
-- ============================================
PRINT '';
PRINT 'Step 3: Granting * to super-admin...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Super Admin gets the global wildcard (*). The other built-in roles are
-- granted by code in 07 (organization roles) and 18 (platform roles).
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000001' AND [PermissionId] = N'20000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'20000000-0000-0000-0000-000000000001', GETUTCDATE(), @SystemUserId);
    -- super-admin gets *
END

PRINT 'Granted * to super-admin';
GO

-- ============================================
-- STEP 4: ORGANIZATION ROLES AND PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 4: Creating organization roles and permissions...';

:r ..\Scripts\SeedData\07_OrganizationRolesPermissions.sql
GO

-- ============================================
-- STEP 5: ADMIN USER
-- ============================================
PRINT '';
PRINT 'Step 5: Creating admin user...';

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
        [Theme],
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
        N'system',
        1,  -- Email confirmed
        2,  -- Status: Inactive (cannot login)
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created system user';
END

-- Create admin user
-- NO PASSWORD IS SEEDED. PasswordHash is left NULL on purpose: a hash committed here is a
-- published credential for every deployment of this system, and LoginCommandHandler rejects a
-- null hash before it reaches the verifier, so nobody can sign in as this account until an
-- operator sets a password out of band.
-- BOOTSTRAP: run the Auth_Setup console app, give it the password you have chosen, and execute
-- the UPDATE statement it prints. Until then this account exists, holds super-admin, and cannot
-- authenticate.
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
        [Theme],
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
        -- No password. See the BOOTSTRAP note above. MustChangePassword stays 1 so the console still
        -- prompts, but the real gate is the null hash, which the server enforces rather than the client.
        NULL,
        N'System',
        N'Administrator',
        N'en',
        N'UTC',
        N'system',
        1,  -- Email confirmed
        1,  -- Status: Active
        1,  -- Must change password on first login
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created admin user with NO password - run Auth_Setup and apply the UPDATE it prints';
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

-- Also assign the basic User role
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
-- STEP 6: EXTERNAL AUTH PROVIDERS
-- ============================================
PRINT '';
PRINT 'Step 6: Creating external auth providers...';

:r ..\Scripts\SeedData\09_ExternalAuthProviders.sql
GO

-- ============================================
-- STEP 7: PLATFORM SETTINGS
-- ============================================
PRINT '';
PRINT 'Step 7: Creating platform settings...';

-- Singleton branding row (name/logo shown across the console and auth screens)
IF NOT EXISTS (SELECT 1 FROM [dbo].[PlatformSettings] WHERE [Id] = '30000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[PlatformSettings] ([Id], [PlatformName])
    VALUES ('30000000-0000-0000-0000-000000000001', N'Auth Console');
    PRINT 'Created default platform settings';
END
ELSE
BEGIN
    PRINT 'Platform settings already exist';
END

-- platform-settings:manage permission (child of the global "*"; the admin
-- role is granted it by code in 18_PlatformPermissions.sql)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'platform-settings:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A2', N'platform-settings:manage', N'Manage Platform Settings', N'Manage platform branding (name and logo)', NULL, N'20000000-0000-0000-0000-000000000001', 1, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created platform-settings:manage permission';
END
GO

-- Platform-wide organizations administration (children of the global "*",
-- granted to admin in 18; distinct from the membership-scoped org:*)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'organizations:read')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A3', N'organizations:read', N'Read All Organizations', N'View any organization on the platform, including ones the caller is not a member of', NULL, N'20000000-0000-0000-0000-000000000001', 1, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created organizations:read permission';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'organizations:manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-0000000000A4', N'organizations:manage', N'Manage All Organizations', N'Administer any organization on the platform, including delete', NULL, N'20000000-0000-0000-0000-000000000001', 1, 0, 1, GETUTCDATE(), '00000000-0000-0000-0000-000000000001');
    PRINT 'Created organizations:manage permission';
END
GO

-- ============================================
-- STEP 8: NOTIFICATION TYPES
-- ============================================
PRINT '';
PRINT 'Step 8: Creating notification types...';

:r ..\Scripts\SeedData\10_NotificationTypes.sql
GO

-- ============================================
-- STEP 9: NOTIFICATION LAYOUTS
-- ============================================
PRINT '';
PRINT 'Step 9: Creating notification layouts...';

:r ..\Scripts\SeedData\11_NotificationLayouts.sql
GO

-- ============================================
-- STEP 10: NOTIFICATION TEMPLATES
-- ============================================
PRINT '';
PRINT 'Step 10: Creating notification templates...';

:r ..\Scripts\SeedData\12_NotificationTemplates.sql
GO

-- ============================================
-- STEP 11: NOTIFICATION PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 11: Creating notification permissions...';

:r ..\Scripts\SeedData\13_NotificationPermissions.sql
:r ..\Scripts\SeedData\14_PrivacyPolicyVersions.sql
:r ..\Scripts\SeedData\15_PrivacyPolicyPermissions.sql
:r ..\Scripts\SeedData\16_PrivacyPolicyContent.sql
GO

-- ============================================
-- STEP 12: SYSTEM SETTINGS PERMISSIONS
-- ============================================
PRINT '';
PRINT 'Step 12: Creating system settings permissions...';

:r ..\Scripts\SeedData\17_SystemSettingsPermissions.sql
GO

-- ============================================
-- STEP 13: PLATFORM PERMISSIONS
-- ============================================
-- Runs LAST on purpose. It seeds the platform codes the API enforces and grants
-- the built-in roles their permissions BY CODE, including codes seeded by the
-- steps above (platform settings, organizations, notifications, privacy policy),
-- so every code it grants must already exist when it runs.
PRINT '';
PRINT 'Step 13: Creating platform permissions and role grants...';

:r ..\Scripts\SeedData\18_PlatformPermissions.sql
GO

-- ============================================
-- COMPLETION
-- ============================================
PRINT '';
PRINT '======================================';
PRINT 'Post-deployment seed data complete!';
PRINT '';
PRINT 'IMPORTANT NOTES:';
PRINT '1. The admin account has no password: run Auth_Setup and apply the UPDATE it prints';
PRINT '2. Review all seed data for your environment';
PRINT '3. Add application-specific roles and permissions from the console';
GO
