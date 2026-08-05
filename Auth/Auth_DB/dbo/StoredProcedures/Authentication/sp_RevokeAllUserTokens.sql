CREATE PROCEDURE [dbo].[sp_RevokeAllUserTokens]
    @UserId UNIQUEIDENTIFIER,
    @RevokedBy UNIQUEIDENTIFIER = NULL,
    @ReasonRevoked NVARCHAR(200) = N'All tokens revoked'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2 = GETUTCDATE();

    -- Captured so the returned count can mean "live sessions actually taken
    -- away", not merely "rows touched". Callers act on that count: the refresh
    -- reuse path emails the account owner only when it is non-zero, and
    -- abandoned lineages that had simply expired must not trigger a security
    -- notice. They are still swept up by the UPDATE below - only the count
    -- excludes them.
    DECLARE @Revoked TABLE ([ExpiresAt] DATETIME2 NOT NULL);

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = @Now,
        [RevokedBy] = @RevokedBy,
        [ReasonRevoked] = @ReasonRevoked
    OUTPUT inserted.[ExpiresAt] INTO @Revoked ([ExpiresAt])
    WHERE [UserId] = @UserId
      AND [RevokedAt] IS NULL;

    -- Also end all active sessions
    UPDATE [dbo].[UserSessions]
    SET [EndedAt] = @Now,
        [EndReason] = @ReasonRevoked
    WHERE [UserId] = @UserId
      AND [EndedAt] IS NULL;

    SELECT COUNT(*) AS [RevokedTokenCount]
    FROM @Revoked
    WHERE [ExpiresAt] > @Now;
END
GO
