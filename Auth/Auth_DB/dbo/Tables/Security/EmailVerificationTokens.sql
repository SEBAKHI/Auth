CREATE TABLE [dbo].[EmailVerificationTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_EmailVerificationTokens_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [OtpHash] NVARCHAR(500) NOT NULL,          -- Argon2id hash of 6-digit OTP
    [Email] NVARCHAR(255) NOT NULL,            -- Email the OTP was sent to
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_EmailVerificationTokens_AttemptCount] DEFAULT 0,      -- Track verification attempts
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_EmailVerificationTokens_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_EmailVerificationTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_EmailVerificationTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores email verification OTPs for user registration
-- OtpHash is Argon2id hash of the 6-digit OTP sent to user
-- Token expires after configurable time period (default 15 minutes)
-- AttemptCount tracks failed verification attempts (max 5)
-- UsedAt is set when the OTP is successfully verified

-- Indexes
CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_UserId]
ON [dbo].[EmailVerificationTokens] ([UserId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_ExpiresAt]
ON [dbo].[EmailVerificationTokens] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO

-- Index for rate limiting queries (count recent tokens by email)
CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_Email_CreatedAt]
ON [dbo].[EmailVerificationTokens] ([Email], [CreatedAt] DESC)
WHERE [UsedAt] IS NULL;
GO

-- Serves the retention sweep, which deletes used and unused rows alike.
-- IX_EmailVerificationTokens_ExpiresAt is filtered to UsedAt IS NULL and so
-- covers only half the set; without this index each batch scans the table.
CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_Cleanup]
ON [dbo].[EmailVerificationTokens] ([ExpiresAt]);
GO
