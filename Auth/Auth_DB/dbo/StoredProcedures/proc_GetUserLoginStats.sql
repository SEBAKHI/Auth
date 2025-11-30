-- =============================================
-- Stored Procedure: Get User Login Statistics
-- Description: Returns login statistics for a user
-- =============================================
CREATE PROCEDURE [dbo].[proc_GetUserLoginStats]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT
        u.[Id],
        u.[Email],
        u.[FirstName],
        u.[LastName],
        u.[LastLoginDate],
        u.[FailedLoginAttempts],
        u.[IsLocked],
        u.[LockedUntil],
        u.[IsActive],
        u.[IsEmailVerified],
        (SELECT COUNT(*) FROM [dbo].[LoginAttempts] WHERE [Email] = u.[Email] AND [IsSuccessful] = 1) AS TotalSuccessfulLogins,
        (SELECT COUNT(*) FROM [dbo].[LoginAttempts] WHERE [Email] = u.[Email] AND [IsSuccessful] = 0) AS TotalFailedLogins,
        (SELECT COUNT(*) FROM [dbo].[RefreshTokens] WHERE [UserId] = u.[Id] AND [IsRevoked] = 0 AND [ExpiresAt] > GETUTCDATE()) AS ActiveTokens,
        (SELECT STRING_AGG(r.Name, ', ') 
         FROM UserRoles ur 
         INNER JOIN Roles r ON ur.RoleId = r.Id 
         WHERE ur.UserId = u.Id AND ur.IsActive = 1 AND r.IsActive = 1) AS Roles
    FROM [dbo].[Users] u
    WHERE u.[Id] = @UserId;
END
GO