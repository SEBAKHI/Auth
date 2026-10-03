CREATE TABLE [dbo].[TwoFactorBindCodes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TwoFactorBindCodes_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,           -- The account binding its first second factor; the code went to its confirmed address
    [CodeHash] NVARCHAR(500) NOT NULL,            -- Keyed hash of the 6-digit code (IOtpHasher, scope "two-factor-bind:{UserId}")
    [ExpiresAt] DATETIME2 NOT NULL,               -- Email:OtpExpirationMinutes after issue
    [UsedAt] DATETIME2 NULL,                      -- Spent by the bind, or superseded by a newer code
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_TwoFactorBindCodes_AttemptCount] DEFAULT 0,
    [IpAddress] NVARCHAR(45) NULL,                -- Audit only, never an authorization input
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_TwoFactorBindCodes_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_TwoFactorBindCodes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorBindCodes_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users]([Id])
);
GO

-- An account that holds no second factor yet must prove it holds its mailbox
-- before it binds its FIRST one (X02 PR B, the owner's RV-P8-4 decision). The
-- code is emailed to the confirmed address and checked before the bind
-- completes; whoever holds only the password cannot bind an authenticator of
-- their own to the account. Proof of the mailbox for that bind and nothing else:
-- the code is never a second factor and never signs anyone in.
--
-- Single use and the attempt cap live in the statements that touch a row
-- (SingleUseCodeStatements): an attempt is reserved before a code is checked,
-- and the code is consumed inside the transaction that switches the factor on,
-- before the factor row. A fresh code supersedes every outstanding one. Every
-- constraint is named and there is no CHECK, so a schema compare reads no drift.
-- Rows are removed by the retention sweep (DataRetention:TwoFactorChallengeDays)
-- and by the user hard-delete purge.

-- Issuance rate limiting (codes per user in a window), and the newest live code.
CREATE NONCLUSTERED INDEX [IX_TwoFactorBindCodes_UserId_CreatedAt]
ON [dbo].[TwoFactorBindCodes] ([UserId], [CreatedAt] DESC);
GO

-- Outstanding codes by expiry, as SecretOperationChallenges keeps them. The
-- retention sweep cannot use it: it also removes used rows, so it scans the
-- table, which holds a few rows per account within the retention window.
CREATE NONCLUSTERED INDEX [IX_TwoFactorBindCodes_ExpiresAt]
ON [dbo].[TwoFactorBindCodes] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO
