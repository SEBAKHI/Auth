CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
    @UserId UNIQUEIDENTIFIER,
    @TokenHash NVARCHAR(100),
    @JwtId NVARCHAR(100),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @DeviceInfo NVARCHAR(500) = NULL,
    @IpAddress NVARCHAR(45) = NULL,
    @ExpiresAt DATETIME2,
    @SessionId UNIQUEIDENTIFIER = NULL
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
        [SessionId],
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
        @SessionId,
        @ApplicationId,
        @DeviceInfo,
        @IpAddress,
        GETUTCDATE(),
        @ExpiresAt
    );

    SELECT @TokenId AS [TokenId];
END
GO
