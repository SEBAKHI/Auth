CREATE PROCEDURE [dbo].[sp_RevokeRefreshToken]
    @TokenHash NVARCHAR(128),
    @RevokedBy UNIQUEIDENTIFIER = NULL,
    @ReasonRevoked NVARCHAR(200) = NULL,
    @ReplacedByToken NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TokenId UNIQUEIDENTIFIER;
    DECLARE @AlreadyRevoked BIT = 0;

    SELECT
        @TokenId = [Id],
        @AlreadyRevoked = CASE WHEN [RevokedAt] IS NOT NULL THEN 1 ELSE 0 END
    FROM [dbo].[RefreshTokens]
    WHERE [TokenHash] = @TokenHash;

    IF @TokenId IS NULL
    BEGIN
        SELECT 0 AS [Success], N'Token not found' AS [Message];
        RETURN;
    END

    IF @AlreadyRevoked = 1
    BEGIN
        SELECT 0 AS [Success], N'Token already revoked' AS [Message];
        RETURN;
    END

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = GETUTCDATE(),
        [RevokedBy] = @RevokedBy,
        [ReasonRevoked] = @ReasonRevoked,
        [ReplacedByToken] = @ReplacedByToken
    WHERE [Id] = @TokenId;

    SELECT 1 AS [Success], N'Token revoked successfully' AS [Message];
END
GO
