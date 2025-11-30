-- =============================================
-- Stored Procedure: proc_AssignDefaultRoleToUser
-- Description: Assigns the default 'User' role to a user
-- =============================================
CREATE PROCEDURE [dbo].[proc_AssignDefaultRoleToUser]
    @UserId UNIQUEIDENTIFIER,
    @AssignedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RoleId UNIQUEIDENTIFIER;
    DECLARE @AssignerId UNIQUEIDENTIFIER;

    -- Get the 'User' role ID
    SELECT @RoleId = Id FROM Roles WHERE Name = 'User' AND IsActive = 1;

    IF @RoleId IS NULL
    BEGIN
        RAISERROR('Default User role not found. Please run proc_CreateDefaultRoles first.', 16, 1);
    RETURN;
    END

    -- Use the user's own ID if AssignedBy is not provided (self-assignment during registration)
    SET @AssignerId = COALESCE(@AssignedBy, @UserId);

    -- Check if user already has this role
    IF EXISTS (
        SELECT 1 FROM UserRoles 
        WHERE UserId = @UserId 
        AND RoleId = @RoleId 
        AND IsActive = 1
    )
    BEGIN
      PRINT 'User already has the default role';
      RETURN;
    END

    -- Assign the role
    INSERT INTO UserRoles (Id, UserId, RoleId, AssignedAt, AssignedBy, IsActive)
        VALUES (
        NEWID(),
        @UserId,
        @RoleId,
        GETUTCDATE(),
        @AssignerId,
        1
        );

    PRINT 'Default User role assigned successfully';
END
GO
