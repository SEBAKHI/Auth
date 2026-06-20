-- Admin User Seed Data
-- Creates the initial super admin user
-- NOTE: Password hash is a placeholder - must be updated with real Argon2id hash!

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
        1,  -- Email confirmed
        2,  -- Status: Inactive (cannot login)
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created system user';
END

-- Create admin user
-- DEFAULT PASSWORD: Admin@123! (MUST be changed on first login!)
-- Hash generated with Argon2id: m=65536,t=3,p=4
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
        -- Working Argon2id hash for the default password 'Admin@123!' (current OWASP params m=19456,t=2,p=1).
        -- MustChangePassword = 1 forces a change on first login. Unpeppered: if Password:Pepper is later
        -- enabled, this hash still verifies and is transparently upgraded (keyid added) on first login.
        N'$argon2id$v=19$m=19456,t=2,p=1$NoKP1nsfZyPf3Hp_V4IHww$_zyvdZiGmyfs87h7_q2f3A.VzxgOfnKVmL5doZ3Kz5Y',
        N'System',
        N'Administrator',
        N'en',
        N'UTC',
        1,  -- Email confirmed
        1,  -- Status: Active
        1,  -- Must change password on first login
        GETUTCDATE(),
        @SystemUserId
    );
    PRINT 'Created admin user (password must be set via application)';
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

-- Also assign User role (for profile access)
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
PRINT 'IMPORTANT: Update the admin password hash using the application before production deployment!';
GO
