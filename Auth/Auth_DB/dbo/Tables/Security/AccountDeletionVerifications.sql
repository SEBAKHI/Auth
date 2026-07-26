CREATE TABLE [dbo].[AccountDeletionVerifications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AccountDeletionVerifications_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [Email] NVARCHAR(255) NOT NULL,            -- Email the OTP was sent to
    [OtpHash] NVARCHAR(500) NOT NULL,          -- Argon2id hash of 6-digit OTP
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_AccountDeletionVerifications_AttemptCount] DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_AccountDeletionVerifications_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_AccountDeletionVerifications] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Deletion re-authentication OTPs: passwordless in-app requests and the public
-- no-login deletion flow. Mirrors EmailVerificationTokens semantics (Argon2id
-- hash, 15-minute expiry, max 5 attempts). UserId is a loose reference (no FK):
-- these rows belong to accounts that are about to be deleted, so nothing may
-- block or outlive the purge. Expired rows are purged by the retention sweep.

-- Rate limiting: count recent codes per email.
CREATE NONCLUSTERED INDEX [IX_AccountDeletionVerifications_Email_CreatedAt]
ON [dbo].[AccountDeletionVerifications] ([Email], [CreatedAt] DESC)
WHERE [UsedAt] IS NULL;
GO

-- Retention sweep: expired unused codes.
CREATE NONCLUSTERED INDEX [IX_AccountDeletionVerifications_ExpiresAt]
ON [dbo].[AccountDeletionVerifications] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_AccountDeletionVerifications_UserId]
ON [dbo].[AccountDeletionVerifications] ([UserId], [CreatedAt] DESC);
GO
