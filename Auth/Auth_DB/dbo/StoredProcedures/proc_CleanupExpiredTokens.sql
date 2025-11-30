-- =============================================
-- Stored Procedure: Cleanup Expired Tokens
-- Description: Removes expired OTP tokens to maintain database performance
-- =============================================
CREATE PROCEDURE [dbo].[proc_CleanupExpiredTokens]
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DeletedCount INT = 0;
    
    -- Delete expired password reset tokens
    DELETE FROM [dbo].[PasswordResetTokens] 
    WHERE [ExpiresAt] < GETUTCDATE() 
     OR ([IsUsed] = 1 AND [UsedAt] < DATEADD(DAY, -30, GETUTCDATE()));
    SET @DeletedCount = @DeletedCount + @@ROWCOUNT;
    
    -- Delete expired email verification tokens
    DELETE FROM [dbo].[EmailVerificationTokens] 
    WHERE [ExpiresAt] < GETUTCDATE() 
        OR ([IsUsed] = 1 AND [UsedAt] < DATEADD(DAY, -30, GETUTCDATE()));
    SET @DeletedCount = @DeletedCount + @@ROWCOUNT;
    
    -- Delete expired and revoked refresh tokens
    DELETE FROM [dbo].[RefreshTokens] 
    WHERE [ExpiresAt] < GETUTCDATE() 
        OR ([IsRevoked] = 1 AND [RevokedAt] < DATEADD(DAY, -30, GETUTCDATE()));
    SET @DeletedCount = @DeletedCount + @@ROWCOUNT;
    
  -- Delete old login attempts (keep last 90 days)
    DELETE FROM [dbo].[LoginAttempts] 
    WHERE [AttemptedAt] < DATEADD(DAY, -90, GETUTCDATE());
    SET @DeletedCount = @DeletedCount + @@ROWCOUNT;
 
    SELECT @DeletedCount AS DeletedRecordsCount;
END
GO