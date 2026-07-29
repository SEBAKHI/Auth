CREATE TABLE [dbo].[UserEncryptionKeys]
(
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [WrappedDek] NVARCHAR(2000) NOT NULL,
    [KeyVersion] INT NOT NULL CONSTRAINT [DF_UserEncryptionKeys_KeyVersion] DEFAULT 1,
    [Algorithm] NVARCHAR(20) NOT NULL CONSTRAINT [DF_UserEncryptionKeys_Algorithm] DEFAULT N'AES-256-GCM',
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserEncryptionKeys_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_UserEncryptionKeys] PRIMARY KEY CLUSTERED ([UserId]),
    CONSTRAINT [FK_UserEncryptionKeys_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- One data-encryption key (DEK) per user, created lazily on the first encrypted
-- write. WrappedDek is the 32-byte AES-256-GCM key wrapped by ASP.NET Data
-- Protection (purpose "UserDek"; precedent: TwoFactorSecretProtector).
-- Deleting the row is the crypto-shredding step of account destruction: every
-- ciphertext under this key (phone number, TOTP secret, provider refresh token)
-- becomes unrecoverable in the database and in all backups.
