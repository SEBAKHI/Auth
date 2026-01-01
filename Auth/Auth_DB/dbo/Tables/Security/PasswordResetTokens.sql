CREATE TABLE [dbo].[PasswordResetTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(500) NOT NULL,   -- Argon2id hash
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PasswordResetTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores password reset tokens for self-service password recovery
-- TokenHash is Argon2id hash of the actual token sent to the user
-- Token expires after a configurable time period (default 1 hour)
-- UsedAt is set when the token is consumed to prevent reuse

-- Indexes
CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_UserId]
ON [dbo].[PasswordResetTokens] ([UserId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_TokenHash]
ON [dbo].[PasswordResetTokens] ([TokenHash])
WHERE [UsedAt] IS NULL AND [ExpiresAt] > GETUTCDATE();
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_ExpiresAt]
ON [dbo].[PasswordResetTokens] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO
