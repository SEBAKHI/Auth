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
        -- Aliased because the reader binds by column name and has no FullName
        -- property: unaliased, Dapper drops the column without complaint and
        -- the sign-in response carries a null display name.
        [FullName] AS [DisplayName],
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
    WHERE [NormalizedEmail] = @NormalizedEmail
      AND [IsDeleted] = 0;
END
GO
