CREATE TABLE [dbo].[TwoFactorAuth]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TwoFactorAuth_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [SecretKey] NVARCHAR(500) NOT NULL,     -- TOTP secret, encrypted at rest via Data Protection (ITwoFactorSecretProtector)
    [RecoveryCodes] NVARCHAR(MAX) NULL,     -- JSON array of hashed recovery codes
    [IsEnabled] BIT NOT NULL CONSTRAINT [DF_TwoFactorAuth_IsEnabled] DEFAULT 0,
    [EnabledAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [FailedAttempts] INT NOT NULL CONSTRAINT [DF_TwoFactorAuth_FailedAttempts] DEFAULT 0,
    [LockedUntil] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_TwoFactorAuth_CreatedAt] DEFAULT GETUTCDATE(),
    [ModifiedAt] DATETIME2 NULL,

    CONSTRAINT [PK_TwoFactorAuth] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorAuth_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_TwoFactorAuth_UserId] UNIQUE ([UserId])
);
GO
