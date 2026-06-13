CREATE TABLE [dbo].[Applications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Applications_Id] DEFAULT NEWID(),
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [BaseUrl] NVARCHAR(500) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [ContactEmail] NVARCHAR(255) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Applications_IsActive] DEFAULT 1,
    [AllowSelfRegistration] BIT NOT NULL CONSTRAINT [DF_Applications_AllowSelfRegistration] DEFAULT 0,
    [RequireEmailVerification] BIT NOT NULL CONSTRAINT [DF_Applications_RequireEmailVerification] DEFAULT 0,
    [RequireTwoFactor] BIT NOT NULL CONSTRAINT [DF_Applications_RequireTwoFactor] DEFAULT 0,
    [SessionTimeoutMinutes] INT NOT NULL CONSTRAINT [DF_Applications_SessionTimeoutMinutes] DEFAULT 60,
    [MaxConcurrentSessions] INT NOT NULL CONSTRAINT [DF_Applications_MaxConcurrentSessions] DEFAULT 5,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Applications_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Applications_Code] UNIQUE ([Code])
);
GO

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Applications_Code]
ON [dbo].[Applications] ([Code])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Applications_IsActive]
ON [dbo].[Applications] ([IsActive]);
GO
