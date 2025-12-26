CREATE PROCEDURE [dbo].[sp_ValidateCredentials]
    @Email NVARCHAR(255),
    @IpAddress NVARCHAR(45) = NULL,
    @UserAgent NVARCHAR(500) = NULL,
    @ApplicationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedEmail NVARCHAR(255) = UPPER(TRIM(@Email));
    DECLARE @UserId UNIQUEIDENTIFIER;
    DECLARE @PasswordHash NVARCHAR(500);
    DECLARE @Status TINYINT;
    DECLARE @IsEmailConfirmed BIT;
    DECLARE @IsTwoFactorEnabled BIT;
    DECLARE @FailedLoginAttempts INT;
    DECLARE @LockoutEndUtc DATETIME2;

    -- Get user info
    SELECT
        @UserId = [Id],
        @PasswordHash = [PasswordHash],
        @Status = [Status],
        @IsEmailConfirmed = [IsEmailConfirmed],
        @IsTwoFactorEnabled = [IsTwoFactorEnabled],
        @FailedLoginAttempts = [FailedLoginAttempts],
        @LockoutEndUtc = [LockoutEndUtc]
    FROM [dbo].[Users]
    WHERE [NormalizedEmail] = @NormalizedEmail
      AND [IsDeleted] = 0;

    -- Return user info for password verification in application layer
    -- (Password verification done in C# with Argon2id)
    IF @UserId IS NOT NULL
    BEGIN
        SELECT
            @UserId AS [UserId],
            @PasswordHash AS [PasswordHash],
            @Status AS [Status],
            @IsEmailConfirmed AS [IsEmailConfirmed],
            @IsTwoFactorEnabled AS [IsTwoFactorEnabled],
            @FailedLoginAttempts AS [FailedLoginAttempts],
            @LockoutEndUtc AS [LockoutEndUtc],
            CASE
                WHEN @Status = 3 THEN 1  -- Locked
                WHEN @LockoutEndUtc IS NOT NULL AND @LockoutEndUtc > GETUTCDATE() THEN 1
                ELSE 0
            END AS [IsLocked],
            CASE
                WHEN @Status = 1 THEN 1  -- Active
                ELSE 0
            END AS [IsActive];
    END
    ELSE
    BEGIN
        -- User not found - return empty result
        SELECT
            NULL AS [UserId],
            NULL AS [PasswordHash],
            NULL AS [Status],
            NULL AS [IsEmailConfirmed],
            NULL AS [IsTwoFactorEnabled],
            0 AS [FailedLoginAttempts],
            NULL AS [LockoutEndUtc],
            0 AS [IsLocked],
            0 AS [IsActive];
    END
END
GO
