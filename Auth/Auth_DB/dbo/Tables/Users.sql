-- =============================================
-- Table: Users
-- Description: Stores user account information
-- =============================================
CREATE TABLE [dbo].[Users] (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Users_Id DEFAULT NEWID(),
    Seq INT IDENTITY(1,1) NOT NULL UNIQUE,
    ExternalId NVARCHAR(128) NULL,

    [Email] NVARCHAR(256) NOT NULL,
    [PasswordHash] NVARCHAR(MAX) NOT NULL,
    [FirstName] NVARCHAR(100) NOT NULL,
    [LastName] NVARCHAR(100) NOT NULL,

    [PreferredLanguage] NVARCHAR(10) NOT NULL CONSTRAINT DF_Users_PreferredLanguage DEFAULT 'en',
    [IsEmailVerified] BIT NOT NULL CONSTRAINT DF_Users_IsEmailVerified DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
    [IsLocked] BIT NOT NULL CONSTRAINT DF_Users_IsLocked DEFAULT 0,
    [FailedLoginAttempts] INT NOT NULL CONSTRAINT DF_Users_FailedLoginAttempts DEFAULT 0,
    [LastLoginDate] DATETIME2(7) NULL,
    [LockedUntil] DATETIME2(7) NULL,
    
    -- Auditing Fields
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2(7) NULL,
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,

    [EmailVerificationToken] NVARCHAR(MAX) NULL,
    [EmailVerificationTokenExpiry] DATETIME2(7) NULL,

    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT UQ_Users_Email UNIQUE NONCLUSTERED ([Email] ASC)
);
GO

-- =============================================
-- Indexes for Users Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_Users_Email 
    ON [dbo].[Users]([Email] ASC);
GO

CREATE NONCLUSTERED INDEX IX_Users_IsActive 
    ON [dbo].[Users]([IsActive] ASC);
GO

CREATE NONCLUSTERED INDEX IX_Users_CreatedAt 
    ON [dbo].[Users]([CreatedAt] DESC);
GO