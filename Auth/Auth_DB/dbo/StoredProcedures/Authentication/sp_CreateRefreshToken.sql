CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
    @UserId UNIQUEIDENTIFIER,
    @TokenHash NVARCHAR(100),
    @JwtId NVARCHAR(100),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @DeviceInfo NVARCHAR(500) = NULL,
    @IpAddress NVARCHAR(45) = NULL,
    @ExpiresAt DATETIME2,
    @SessionId UNIQUEIDENTIFIER = NULL,
    -- Last, with a default: the previous API's positional EXEC passes eight
    -- arguments and keeps working against this procedure.
    @Scope NVARCHAR(200) = NULL,
    -- OI-154, last with a default for the same reason: the previous API passes
    -- nine arguments. The IdP session this token's chain was born from.
    @IdpSessionId UNIQUEIDENTIFIER = NULL,
    -- OI-171, last with a default for the same reason: the previous API passes
    -- ten arguments. 1 = the token is bound to its application's sign-in key.
    @RequiresClientAuthentication BIT = NULL
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
        [ExpiresAt],
        [Scope],
        [IdpSessionId],
        [RequiresClientAuthentication]
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
        @ExpiresAt,
        @Scope,
        @IdpSessionId,
        @RequiresClientAuthentication
    );

    SELECT @TokenId AS [TokenId];
END
GO
