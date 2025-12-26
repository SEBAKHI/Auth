CREATE TABLE [dbo].[Applications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [BaseUrl] NVARCHAR(500) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [ContactEmail] NVARCHAR(255) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [AllowSelfRegistration] BIT NOT NULL DEFAULT 0,
    [RequireTwoFactor] BIT NOT NULL DEFAULT 0,
    [SessionTimeoutMinutes] INT NOT NULL DEFAULT 60,
    [MaxConcurrentSessions] INT NOT NULL DEFAULT 5,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
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
