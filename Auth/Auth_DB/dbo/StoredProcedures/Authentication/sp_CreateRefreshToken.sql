CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
    @UserId UNIQUEIDENTIFIER,
    @Token NVARCHAR(500),
    @TokenHash NVARCHAR(128),
    @JwtId NVARCHAR(100),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @DeviceInfo NVARCHAR(500) = NULL,
    @IpAddress NVARCHAR(45) = NULL,
    @ExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TokenId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO [dbo].[RefreshTokens]
    (
        [Id],
        [UserId],
        [Token],
        [TokenHash],
        [JwtId],
        [ApplicationId],
        [DeviceInfo],
        [IpAddress],
        [CreatedAt],
        [ExpiresAt]
    )
    VALUES
    (
        @TokenId,
        @UserId,
        @Token,
        @TokenHash,
        @JwtId,
        @ApplicationId,
        @DeviceInfo,
        @IpAddress,
        GETUTCDATE(),
        @ExpiresAt
    );

    SELECT @TokenId AS [TokenId];
END
GO
