CREATE TABLE [dbo].[PasswordResetTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PasswordResetTokens_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(500) NOT NULL,   -- HMAC-SHA256 hash (base64 encoded, ~44 chars)
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PasswordResetTokens_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PasswordResetTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores password reset tokens for self-service password recovery
-- TokenHash is the HMAC-SHA256 hash of the 256-bit token sent to the user.
--   The hash is deterministic on purpose: the token alone identifies the row on
--   redemption, so no email address is needed. Argon2id would be salted (and thus
--   unindexable) for no benefit - the token is high-entropy and cannot be guessed.
--   Column stays NVARCHAR(500) from the previous Argon2id scheme; a hash is ~44 chars.
-- Token expires after a configurable time period (Email:ResetTokenExpirationMinutes)
-- UsedAt is set when the token is consumed to prevent reuse

-- Indexes
CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_UserId]
ON [dbo].[PasswordResetTokens] ([UserId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_TokenHash]
ON [dbo].[PasswordResetTokens] ([TokenHash])
WHERE [UsedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_ExpiresAt]
ON [dbo].[PasswordResetTokens] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO

-- The retention sweep deletes rows past their grace window regardless of
-- UsedAt, so IX_PasswordResetTokens_ExpiresAt above cannot serve it: that index
-- is filtered to UNUSED rows, which is the opposite set. Without this one the
-- sweep scans the whole table on every batch.
CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_Cleanup]
ON [dbo].[PasswordResetTokens] ([ExpiresAt]);
GO
