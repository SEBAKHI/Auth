CREATE TABLE [dbo].[Users]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Users_Id] DEFAULT NEWID(),
    [Username] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [NormalizedEmail] NVARCHAR(255) NOT NULL,
    [PasswordHash] NVARCHAR(500) NULL,
    [FirstName] NVARCHAR(100) NULL,
    [LastName] NVARCHAR(100) NULL,
    [FullName] AS (ISNULL([FirstName], N'') + N' ' + ISNULL([LastName], N'')) PERSISTED NOT NULL,
    [PhoneNumber] NVARCHAR(20) NULL,
    [ProfileImageUrl] NVARCHAR(500) NULL,
    [PreferredLanguage] NVARCHAR(10) NOT NULL CONSTRAINT [DF_Users_PreferredLanguage] DEFAULT N'en',
    [TimeZone] NVARCHAR(50) NOT NULL CONSTRAINT [DF_Users_TimeZone] DEFAULT N'UTC',
    [Theme] NVARCHAR(10) NOT NULL CONSTRAINT [DF_Users_Theme] DEFAULT N'system',
    [IsEmailConfirmed] BIT NOT NULL CONSTRAINT [DF_Users_IsEmailConfirmed] DEFAULT 0,
    [IsPhoneConfirmed] BIT NOT NULL CONSTRAINT [DF_Users_IsPhoneConfirmed] DEFAULT 0,
    [IsTwoFactorEnabled] BIT NOT NULL CONSTRAINT [DF_Users_IsTwoFactorEnabled] DEFAULT 0,
    [Status] TINYINT NOT NULL CONSTRAINT [DF_Users_Status] DEFAULT 1,
    [FailedLoginAttempts] INT NOT NULL CONSTRAINT [DF_Users_FailedLoginAttempts] DEFAULT 0,
    [LockoutEndUtc] DATETIME2 NULL,
    [LastLoginUtc] DATETIME2 NULL,
    [LastLoginIp] NVARCHAR(45) NULL,
    [LastPasswordChangeUtc] DATETIME2 NULL,
    [MustChangePassword] BIT NOT NULL CONSTRAINT [DF_Users_MustChangePassword] DEFAULT 0,
    [PasswordExpiresUtc] DATETIME2 NULL,
    [SecurityStamp] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Users_SecurityStamp] DEFAULT CONVERT(NVARCHAR(100), NEWID()),
    [ConcurrencyStamp] NVARCHAR(100) NOT NULL CONSTRAINT [DF_Users_ConcurrencyStamp] DEFAULT CONVERT(NVARCHAR(100), NEWID()),
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Users_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Users_IsDeleted] DEFAULT 0,
    [DeletedAt] DATETIME2 NULL,
    [DeletedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Users_Username] UNIQUE ([Username]),
    CONSTRAINT [UQ_Users_NormalizedEmail] UNIQUE ([NormalizedEmail]),
    CONSTRAINT [CK_Users_Status] CHECK ([Status] IN (1, 2, 3, 4))
);
GO

-- Status values: 1=Active, 2=Inactive, 3=Locked, 4=PendingVerification
-- PreferredLanguage values: 'en', 'ar', 'tr', 'fr', 'zh', 'ur', 'fa'
-- Theme values: 'light', 'dark', 'system'

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Users_NormalizedEmail]
ON [dbo].[Users] ([NormalizedEmail])
WHERE [IsDeleted] = 0;
GO

CREATE NONCLUSTERED INDEX [IX_Users_Username]
ON [dbo].[Users] ([Username])
WHERE [IsDeleted] = 0;
GO

CREATE NONCLUSTERED INDEX [IX_Users_Status]
ON [dbo].[Users] ([Status])
WHERE [IsDeleted] = 0;
GO

CREATE NONCLUSTERED INDEX [IX_Users_CreatedAt]
ON [dbo].[Users] ([CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_Users_LastLoginUtc]
ON [dbo].[Users] ([LastLoginUtc] DESC)
WHERE [IsDeleted] = 0;
GO
