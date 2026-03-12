CREATE TABLE [dbo].[Users]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Username] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [NormalizedEmail] NVARCHAR(255) NOT NULL,
    [PasswordHash] NVARCHAR(500) NULL,
    [FirstName] NVARCHAR(100) NULL,
    [LastName] NVARCHAR(100) NULL,
    [FullName] AS (ISNULL([FirstName], N'') + N' ' + ISNULL([LastName], N'')) PERSISTED,
    [PhoneNumber] NVARCHAR(20) NULL,
    [ProfileImageUrl] NVARCHAR(500) NULL,
    [PreferredLanguage] NVARCHAR(10) NOT NULL DEFAULT N'en',
    [TimeZone] NVARCHAR(50) NOT NULL DEFAULT N'UTC',
    [IsEmailConfirmed] BIT NOT NULL DEFAULT 0,
    [IsPhoneConfirmed] BIT NOT NULL DEFAULT 0,
    [IsTwoFactorEnabled] BIT NOT NULL DEFAULT 0,
    [Status] TINYINT NOT NULL DEFAULT 1,
    [FailedLoginAttempts] INT NOT NULL DEFAULT 0,
    [LockoutEndUtc] DATETIME2 NULL,
    [LastLoginUtc] DATETIME2 NULL,
    [LastLoginIp] NVARCHAR(45) NULL,
    [LastPasswordChangeUtc] DATETIME2 NULL,
    [MustChangePassword] BIT NOT NULL DEFAULT 0,
    [PasswordExpiresUtc] DATETIME2 NULL,
    [SecurityStamp] NVARCHAR(100) NOT NULL DEFAULT CAST(NEWID() AS NVARCHAR(100)),
    [ConcurrencyStamp] NVARCHAR(100) NOT NULL DEFAULT CAST(NEWID() AS NVARCHAR(100)),
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
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
