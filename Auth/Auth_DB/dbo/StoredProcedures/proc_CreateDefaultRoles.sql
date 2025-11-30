-- =============================================
-- Stored Procedure: proc_CreateDefaultRoles
-- Description: Creates default system roles if they don't exist
-- =============================================
CREATE PROCEDURE [dbo].[proc_CreateDefaultRoles]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
    DECLARE @CurrentDate DATETIME2(7) = GETUTCDATE();

    -- Create Admin Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Admin')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'Admin',
            'System Administrator with full access to all features',
            1,
            @CurrentDate,
            @SystemUserId
        );
        PRINT 'Admin role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'Admin role already exists';
    END

    -- Create User Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'User')
    BEGIN
    INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'User',
            'Standard user with basic access',
            1,
            @CurrentDate,
            @SystemUserId
        );
        PRINT 'User role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'User role already exists';
    END

    -- Create Agency Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Agency')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'Agency',
            'Agency with elevated privileges for team management',
            1,
            @CurrentDate,
            @SystemUserId
            );
            PRINT 'Agency role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'Agency role already exists';
    END

    -- Create University Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'University')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'University',
            'University staff member with access to academic resources',
            1,
            @CurrentDate,
            @SystemUserId
            );
        PRINT 'University role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'University role already exists';
    END

    -- Create Student Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Student')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
   VALUES (
         NEWID(),
            'Student',
            'Student user with access to learning materials',
            1,
            @CurrentDate,
            @SystemUserId
            );
        PRINT 'Student role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'Student role already exists';
    END

    -- Return created roles
    SELECT 
        Id,
        Name,
        Description,
        IsActive,
        CreatedAt,
        CreatedBy
    FROM Roles
    WHERE Name IN ('Admin', 'User', 'Agency', 'University', 'Student')
    ORDER BY Name;
END
GO
