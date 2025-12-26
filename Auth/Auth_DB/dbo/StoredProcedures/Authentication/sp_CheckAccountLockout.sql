CREATE PROCEDURE [dbo].[sp_CheckAccountLockout]
    @UserId UNIQUEIDENTIFIER,
    @MaxFailedAttempts INT = 5,
    @LockoutDurationMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FailedLoginAttempts INT;
    DECLARE @LockoutEndUtc DATETIME2;
    DECLARE @Status TINYINT;
    DECLARE @IsLocked BIT = 0;
    DECLARE @ShouldLock BIT = 0;
    DECLARE @RemainingAttempts INT;
    DECLARE @LockoutRemainingMinutes INT = 0;

    SELECT
        @FailedLoginAttempts = [FailedLoginAttempts],
        @LockoutEndUtc = [LockoutEndUtc],
        @Status = [Status]
    FROM [dbo].[Users]
    WHERE [Id] = @UserId
      AND [IsDeleted] = 0;

    -- Check if account is already locked
    IF @Status = 3 OR (@LockoutEndUtc IS NOT NULL AND @LockoutEndUtc > GETUTCDATE())
    BEGIN
        SET @IsLocked = 1;
        IF @LockoutEndUtc IS NOT NULL
        BEGIN
            SET @LockoutRemainingMinutes = DATEDIFF(MINUTE, GETUTCDATE(), @LockoutEndUtc);
            IF @LockoutRemainingMinutes < 0
                SET @LockoutRemainingMinutes = 0;
        END
    END

    -- Check if lockout has expired
    IF @LockoutEndUtc IS NOT NULL AND @LockoutEndUtc <= GETUTCDATE()
    BEGIN
        -- Clear lockout
        UPDATE [dbo].[Users]
        SET [LockoutEndUtc] = NULL,
            [FailedLoginAttempts] = 0,
            [Status] = CASE WHEN [Status] = 3 THEN 1 ELSE [Status] END,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;

        SET @IsLocked = 0;
        SET @FailedLoginAttempts = 0;
    END

    -- Check if should lock now
    IF @FailedLoginAttempts >= @MaxFailedAttempts AND @IsLocked = 0
    BEGIN
        SET @ShouldLock = 1;
        SET @LockoutEndUtc = DATEADD(MINUTE, @LockoutDurationMinutes, GETUTCDATE());

        UPDATE [dbo].[Users]
        SET [LockoutEndUtc] = @LockoutEndUtc,
            [Status] = 3,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;

        SET @IsLocked = 1;
        SET @LockoutRemainingMinutes = @LockoutDurationMinutes;
    END

    SET @RemainingAttempts = @MaxFailedAttempts - @FailedLoginAttempts;
    IF @RemainingAttempts < 0
        SET @RemainingAttempts = 0;

    SELECT
        @IsLocked AS [IsLocked],
        @FailedLoginAttempts AS [FailedAttempts],
        @RemainingAttempts AS [RemainingAttempts],
        @LockoutEndUtc AS [LockoutEndUtc],
        @LockoutRemainingMinutes AS [LockoutRemainingMinutes],
        @ShouldLock AS [JustLocked];
END
GO
