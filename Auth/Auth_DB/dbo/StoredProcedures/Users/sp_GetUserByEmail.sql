CREATE PROCEDURE [dbo].[sp_GetUserByEmail]
    @Email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedEmail NVARCHAR(255) = UPPER(TRIM(@Email));

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
        [IsEmailConfirmed],
        [IsPhoneConfirmed],
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
    WHERE [NormalizedEmail] = @NormalizedEmail
      AND [IsDeleted] = 0;
END
GO
