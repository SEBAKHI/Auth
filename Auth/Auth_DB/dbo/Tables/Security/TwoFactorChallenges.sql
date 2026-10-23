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
    [PrimaryMethod] INT NULL,                  -- P3 deploy-1 batch: the first factor that opened this challenge; NULL = issued before the column existed (S08)
    -- S24: the code emailed for this challenge. Appended last, the NOT NULL columns
    -- with named defaults, so the publish is a plain ALTER TABLE ADD.
    [EmailCodeHash] NVARCHAR(200) NULL,        -- keyed hashes of every code emailed for this challenge (at most three), ';'-separated; the last is the live one
    [EmailCodeSentAt] DATETIME2 NULL,          -- when the last code was emailed
    [EmailCodeSendCount] INT NOT NULL CONSTRAINT [DF_TwoFactorChallenges_EmailCodeSendCount] DEFAULT 0,  -- codes emailed for this challenge
    [Purpose] TINYINT NOT NULL CONSTRAINT [DF_TwoFactorChallenges_Purpose] DEFAULT 1,  -- TwoFactorChallengePurpose; 1 = a sign-in, every row written before S24

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
