CREATE PROCEDURE [dbo].[sp_GetUserById]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [Username],
        [Email],
        [NormalizedEmail],
        [PasswordHash],
        [FirstName],
        [LastName],
        [FullName],
        [PhoneNumber],
        [ProfileImageUrl],
        [PreferredLanguage],
        [TimeZone],
        [Theme],
        [IsEmailConfirmed] AS [EmailConfirmed],
        [IsPhoneConfirmed] AS [PhoneConfirmed],
        [IsTwoFactorEnabled] AS [TwoFactorEnabled],
        [Status],
        [FailedLoginAttempts],
        [LockoutEndUtc] AS [LockoutEnd],
        [LastLoginUtc] AS [LastLoginAt],
        [LastLoginIp],
        [LastPasswordChangeUtc] AS [PasswordChangedAt],
        [MustChangePassword],
        [PasswordExpiresUtc],
        [SecurityStamp],
        [ConcurrencyStamp],
        [CreatedAt],
        [CreatedBy],
        [ModifiedAt],
        [ModifiedBy],
        [IsDeleted],
        [DeletedAt],
        [DeletedBy]
    FROM [dbo].[Users]
    WHERE [Id] = @UserId
      AND [IsDeleted] = 0;
END
GO
