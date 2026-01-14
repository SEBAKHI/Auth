CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
    @UserId UNIQUEIDENTIFIER,
    @TokenHash NVARCHAR(100),
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
