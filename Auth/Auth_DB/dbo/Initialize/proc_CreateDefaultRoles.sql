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

    -- Create CMS_Admin Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'CMS_Admin')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'CMS_Admin',
            'CMS System Administrator with full access to all features',
            1,
            @CurrentDate,
            @SystemUserId
        );
        PRINT 'CMS_Admin role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'CMS_Admin role already exists';
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

    -- Create Agent Role
    IF NOT EXISTS (SELECT 1 FROM Roles WHERE Name = 'Agent')
    BEGIN
        INSERT INTO Roles (Id, Name, Description, IsActive, CreatedAt, CreatedBy)
        VALUES (
            NEWID(),
            'Agent',
            'Agent with elevated privileges for managing users and operations',
            1,
            @CurrentDate,
            @SystemUserId
        );
        PRINT 'Agent role created successfully';
    END
    ELSE
    BEGIN
        PRINT 'Agent role already exists';
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

    -- Return created roles
    SELECT 
        Id,
        Name,
        Description,
        IsActive,
        CreatedAt,
        CreatedBy
    FROM Roles
    WHERE Name IN ('Admin', 'User', 'Agent', 'University')
    ORDER BY Name;
END