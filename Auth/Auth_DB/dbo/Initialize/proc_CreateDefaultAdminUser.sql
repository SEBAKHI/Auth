-- =============================================
-- Insert Default Admin User
-- Password: (CHANGE IT IN PRODUCTION!)
-- =============================================
CREATE PROCEDURE [dbo].[proc_CreateDefaultAdminUser]
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @AdminUserId UNIQUEIDENTIFIER;
    DECLARE @AdminRoleId UNIQUEIDENTIFIER;
    
    -- Check if admin user exists
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'it@unitededucation.com')
    BEGIN
        PRINT 'Creating default admin user...';
    
        -- Create new GUID for admin user
        SET @AdminUserId = NEWID();
    
        INSERT INTO [dbo].[Users] (
            Id,
            [Email], 
            [PasswordHash], 
            [FirstName], 
            [LastName], 
            [IsEmailVerified], 
            [IsActive],
            [CreatedAt],
            [CreatedBy]
            )
        VALUES (
            @AdminUserId,
            'it@unitededucation.com',
            '$2a$12$emvHLSR7Xm.NBrRBNmXupuDfKvADgDGAhRdcH7UXbLUWA0Aj.NRHK', -- BCrypt hash 12 round for 'Admin@united!1122...'
            'System',
            'Admin',
            1,
            1,
            GETUTCDATE(),
            '00000000-0000-0000-0000-000000000000' -- System user
            );
    
        PRINT 'Default admin user created successfully!';
   
        -- Assign Admin role
        SELECT @AdminRoleId = Id FROM Roles WHERE Name = 'Admin' AND IsActive = 1;
        
        IF @AdminRoleId IS NOT NULL
        BEGIN
            INSERT INTO UserRoles (Id, UserId, RoleId, AssignedAt, AssignedBy, IsActive)
            VALUES (
                NEWID(),
                @AdminUserId,
                @AdminRoleId,
                GETUTCDATE(),
                '00000000-0000-0000-0000-000000000000', -- System user
                1
                );
            PRINT 'Admin role assigned successfully!';
        END
        ELSE
        BEGIN
          PRINT 'WARNING: Admin role not found. Please run proc_CreateDefaultRoles first!';
        END
        
        PRINT '========================================';
        PRINT 'Email: it@unitededucation.com';
        PRINT 'Password: Admin@united!1122...';
        PRINT '========================================';
        PRINT 'WARNING: Please change the default admin password immediately!';
    END
    ELSE
    BEGIN
        PRINT 'Default admin user already exists. Skipping creation.';
    END
END
GO