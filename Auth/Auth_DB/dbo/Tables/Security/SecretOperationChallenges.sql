CREATE TABLE [dbo].[SecretOperationChallenges]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_SecretOperationChallenges_Id] DEFAULT NEWID(),
    [RequestedBy] UNIQUEIDENTIFIER NOT NULL,      -- Administrator the code was emailed to
    [Operation] TINYINT NOT NULL,                 -- SecretOperation enum; the approval is valid for this value only
    [PayloadHash] NVARCHAR(128) NULL,             -- SHA-256 hex of imported key material; NULL for the generate operations
    [CodeHash] NVARCHAR(500) NOT NULL,            -- Argon2id hash of the 6-digit code
    [ExpiresAt] DATETIME2 NOT NULL,               -- Code entry window
    [VerifiedAt] DATETIME2 NULL,
    [ApprovalExpiresAt] DATETIME2 NULL,           -- Spend window, opened by verification
    [UsedAt] DATETIME2 NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_SecretOperationChallenges_AttemptCount] DEFAULT 0,
    [IpAddress] NVARCHAR(45) NULL,                -- Audit only, never an authorization input
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SecretOperationChallenges_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_SecretOperationChallenges] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_SecretOperationChallenges_Users] FOREIGN KEY ([RequestedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [CK_SecretOperationChallenges_Operation] CHECK ([Operation] IN (1, 2, 3, 4, 5, 6))
);
GO

-- Step-up re-authentication for destructive secret operations: regenerating or
-- importing the RSA signing key, the refresh-token HMAC key, or the gateway
-- token. A code is emailed to the requesting administrator and must be entered
-- before the operation is costed or executed. Mirrors OwnershipTransferCodes
-- semantics (Argon2id hash, short expiry, max 5 attempts).
--
-- The row binds the approval to the administrator, the exact operation, and —
-- for imports — a digest of the key material, all re-checked when it is spent.
-- Two windows: ExpiresAt bounds code entry, ApprovalExpiresAt bounds how long a
-- verified approval stays spendable (5 minutes, deliberately not configurable).
-- Rows are purged by the retention sweep once BOTH windows have closed, so a
-- code verified just before ExpiresAt keeps its approval for the full five
-- minutes.

-- Issuance rate limiting: count recent codes per administrator.
CREATE NONCLUSTERED INDEX [IX_SecretOperationChallenges_RequestedBy_CreatedAt]
ON [dbo].[SecretOperationChallenges] ([RequestedBy], [CreatedAt] DESC);
GO

-- Superseding outstanding codes on a fresh issue, and the retention sweep.
CREATE NONCLUSTERED INDEX [IX_SecretOperationChallenges_ExpiresAt]
ON [dbo].[SecretOperationChallenges] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO
