CREATE PROCEDURE [dbo].[sp_RevokeAllUserTokens]
    @UserId UNIQUEIDENTIFIER,
    @RevokedBy UNIQUEIDENTIFIER = NULL,
    @ReasonRevoked NVARCHAR(200) = N'All tokens revoked'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RevokedCount INT;

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = GETUTCDATE(),
        [RevokedBy] = @RevokedBy,
        [ReasonRevoked] = @ReasonRevoked
    WHERE [UserId] = @UserId
      AND [RevokedAt] IS NULL;

    SET @RevokedCount = @@ROWCOUNT;

    -- Also end all active sessions
    UPDATE [dbo].[UserSessions]
    SET [EndedAt] = GETUTCDATE(),
        [EndReason] = @ReasonRevoked
    WHERE [UserId] = @UserId
      AND [EndedAt] IS NULL;

    SELECT @RevokedCount AS [RevokedTokenCount];
END
GO
