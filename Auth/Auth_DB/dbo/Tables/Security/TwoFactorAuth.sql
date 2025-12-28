CREATE TABLE [dbo].[TwoFactorAuth]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [SecretKey] NVARCHAR(200) NOT NULL,     -- TOTP secret (encrypted)
    [RecoveryCodes] NVARCHAR(MAX) NULL,     -- JSON array of hashed recovery codes
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [EnabledAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [FailedAttempts] INT NOT NULL DEFAULT 0,
    [LockedUntil] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedAt] DATETIME2 NULL,

    CONSTRAINT [PK_TwoFactorAuth] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorAuth_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_TwoFactorAuth_UserId] UNIQUE ([UserId])
);
GO
