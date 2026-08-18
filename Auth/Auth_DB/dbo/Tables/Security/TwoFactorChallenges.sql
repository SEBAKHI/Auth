CREATE TABLE [dbo].[TwoFactorChallenges]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TwoFactorChallenges_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(500) NOT NULL,        -- HMAC-SHA256 hash of the opaque challenge token
    [IpAddress] NVARCHAR(45) NULL,             -- IP that initiated the login (IPv4/IPv6)
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_TwoFactorChallenges_AttemptCount] DEFAULT 0,       -- Track verification attempts
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_TwoFactorChallenges_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_TwoFactorChallenges] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorChallenges_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores short-lived login-time 2FA challenges issued after password verification.
-- The client receives an opaque high-entropy token; only its keyed hash is stored.
-- Challenge expires after 5 minutes and is single-use (UsedAt set on success).
-- AttemptCount tracks failed code verifications (max 5 per challenge)

-- Lookup by presented token hash
CREATE UNIQUE NONCLUSTERED INDEX [UX_TwoFactorChallenges_TokenHash]
ON [dbo].[TwoFactorChallenges] ([TokenHash]);
GO

CREATE NONCLUSTERED INDEX [IX_TwoFactorChallenges_UserId]
ON [dbo].[TwoFactorChallenges] ([UserId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_TwoFactorChallenges_ExpiresAt]
ON [dbo].[TwoFactorChallenges] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO

-- Serves the retention sweep, which deletes used and unused challenges alike.
-- IX_TwoFactorChallenges_ExpiresAt is filtered to UsedAt IS NULL, the opposite
-- of what the sweep mostly removes.
CREATE NONCLUSTERED INDEX [IX_TwoFactorChallenges_Cleanup]
ON [dbo].[TwoFactorChallenges] ([ExpiresAt]);
GO
