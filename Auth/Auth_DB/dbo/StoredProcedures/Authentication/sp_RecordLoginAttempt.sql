CREATE PROCEDURE [dbo].[sp_RecordLoginAttempt]
    @UserId UNIQUEIDENTIFIER = NULL,
    @Username NVARCHAR(255),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @IpAddress NVARCHAR(45),
    @UserAgent NVARCHAR(500) = NULL,
    @IsSuccessful BIT,
    @FailureReason NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[LoginAttempts]
    (
        [UserId],
        [Username],
        [ApplicationId],
        [IpAddress],
        [UserAgent],
        [AttemptedAt],
        [IsSuccessful],
        [FailureReason]
    )
    VALUES
    (
        @UserId,
        @Username,
        @ApplicationId,
        @IpAddress,
        @UserAgent,
        GETUTCDATE(),
        @IsSuccessful,
        @FailureReason
    );

    -- If failed login, increment failed attempts counter
    IF @IsSuccessful = 0 AND @UserId IS NOT NULL
    BEGIN
        UPDATE [dbo].[Users]
        SET [FailedLoginAttempts] = [FailedLoginAttempts] + 1,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;
    END

    -- If successful login, reset failed attempts and update last login info
    IF @IsSuccessful = 1 AND @UserId IS NOT NULL
    BEGIN
        UPDATE [dbo].[Users]
        SET [FailedLoginAttempts] = 0,
            [LastLoginUtc] = GETUTCDATE(),
            [LastLoginIp] = @IpAddress,
            [LockoutEndUtc] = NULL,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;
    END
END
GO
