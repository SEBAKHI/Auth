CREATE PROCEDURE [dbo].[proc_InitializeDB_InsertDefaultRecords]
AS

-- =============================================
-- Database Initialization Script
-- Description: Initialize the authentication database with default data
-- Run this script after database creation
-- =============================================

SET NOCOUNT ON;
    

PRINT '========================================';
PRINT 'Starting United Education Auth Database Initialization';
PRINT '========================================';
PRINT '';

-- Step 1: Create Default Roles
PRINT 'Step 1: Creating default roles...';
EXEC [dbo].[proc_CreateDefaultRoles];
PRINT '';

-- Step 2: Create Default Admin User
PRINT 'Step 2: Creating default admin user...';
EXEC [dbo].[proc_CreateDefaultAdminUser];
PRINT '';

-- Step 3: Verify Installation
PRINT 'Step 3: Verifying installation...';
PRINT '';

-- Check Roles
DECLARE @RoleCount INT;
SELECT @RoleCount = COUNT(*) FROM [dbo].[Roles];
PRINT 'Total Roles Created: ' + CAST(@RoleCount AS NVARCHAR(10));

-- Check Users
DECLARE @UserCount INT;
SELECT @UserCount = COUNT(*) FROM [dbo].[Users];
PRINT 'Total Users Created: ' + CAST(@UserCount AS NVARCHAR(10));

-- Check UserRoles
DECLARE @UserRoleCount INT;
SELECT @UserRoleCount = COUNT(*) FROM [dbo].[UserRoles];
PRINT 'Total User-Role Assignments: ' + CAST(@UserRoleCount AS NVARCHAR(10));

PRINT '';
PRINT '========================================';
PRINT 'Database Initialization Complete!';
PRINT '========================================';
PRINT '';
PRINT 'IMPORTANT: Please change the default admin password immediately!';
PRINT 'Default Admin Credentials:';
PRINT '  Email: it@unitededucation.com';
PRINT '  Password: Admin@united!1122...';
PRINT '';
PRINT 'Available Roles:';
SELECT [Name], [Description], [IsActive] FROM [dbo].[Roles] ORDER BY [Name];
GO

