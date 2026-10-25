CREATE TABLE [dbo].[TwoFactorAuth]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TwoFactorAuth_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [SecretKey] NVARCHAR(500) NULL,         -- TOTP secret, encrypted at rest via Data Protection (ITwoFactorSecretProtector); NULL on a row whose factor is the emailed code (S24)
    [RecoveryCodes] NVARCHAR(MAX) NULL,     -- JSON array of hashed recovery codes
    [IsEnabled] BIT NOT NULL CONSTRAINT [DF_TwoFactorAuth_IsEnabled] DEFAULT 0,
    [EnabledAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [FailedAttempts] INT NOT NULL CONSTRAINT [DF_TwoFactorAuth_FailedAttempts] DEFAULT 0,
    [LockedUntil] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_TwoFactorAuth_CreatedAt] DEFAULT GETUTCDATE(),
    [ModifiedAt] DATETIME2 NULL,
    -- P3 deploy-1 batch. Appended last, nullable and without a DEFAULT, so the
    -- publish is a plain ALTER TABLE ADD and code that predates them is unaffected.
    [LastUsedTimeStep] BIGINT NULL,             -- absolute TOTP step (unix seconds / 30) of the last accepted code; a code at or below it is a replay (X01)
    [PendingSecretKey] NVARCHAR(500) NULL,      -- replacement TOTP secret awaiting confirmation, encrypted like SecretKey (S08)
    [PendingSecretCreatedAt] DATETIME2 NULL,    -- when PendingSecretKey was issued (S08)
    -- S24: the code emailed at sign-in as the second factor. Appended last, each
    -- NOT NULL column with a named default, so the publish is a plain ALTER TABLE ADD.
    [EmailCodeEnabledAt] DATETIME2 NULL,        -- when the emailed code became this account's second factor; NULL = it is not
    [EmailFailedAttempts] INT NOT NULL CONSTRAINT [DF_TwoFactorAuth_EmailFailedAttempts] DEFAULT 0,  -- consecutive wrong emailed codes, counted apart from FailedAttempts
    [EmailLockedUntil] DATETIME2 NULL,          -- the emailed code's timed lock
    [RecoveryCodesQualified] BIT NOT NULL CONSTRAINT [DF_TwoFactorAuth_RecoveryCodesQualified] DEFAULT 0,  -- 1 = the recovery codes were issued with the authenticator app or in a two-factor session, so one counts as a second factor

    CONSTRAINT [PK_TwoFactorAuth] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorAuth_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_TwoFactorAuth_UserId] UNIQUE ([UserId]),
    -- One second factor per account (S24): the authenticator app (IsEnabled = 1) and
    -- the emailed code are never both bound, so binding one must clear the other.
    CONSTRAINT [CK_TwoFactorAuth_SingleFactor] CHECK (NOT ([IsEnabled] = 1 AND [EmailCodeEnabledAt] IS NOT NULL)),
    -- An enabled authenticator app always has its secret: only an emailed-code row has none.
    CONSTRAINT [CK_TwoFactorAuth_EnabledHasSecret] CHECK ([IsEnabled] = 0 OR [SecretKey] IS NOT NULL)
);
GO
