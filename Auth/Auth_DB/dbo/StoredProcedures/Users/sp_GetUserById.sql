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
        -- Aliased because the reader binds by column name and has no FullName
        -- property: unaliased, Dapper drops the column without complaint and
        -- every profile and detail page renders a blank display name.
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
    WHERE [Id] = @UserId
      AND [IsDeleted] = 0;
END
GO
