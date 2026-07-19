CREATE TABLE [dbo].[OwnershipTransferCodes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OwnershipTransferCodes_Id] DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [TargetUserId] UNIQUEIDENTIFIER NOT NULL,     -- Prospective new owner the code was emailed to
    [InitiatedBy] UNIQUEIDENTIFIER NOT NULL,      -- Owner who initiated the transfer
    [CodeHash] NVARCHAR(500) NOT NULL,            -- Argon2id hash of the 6-digit code
    [ExpiresAt] DATETIME2 NOT NULL,
    [UsedAt] DATETIME2 NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_OwnershipTransferCodes_AttemptCount] DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OwnershipTransferCodes_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_OwnershipTransferCodes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OwnershipTransferCodes_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OwnershipTransferCodes_Users_Target] FOREIGN KEY ([TargetUserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OwnershipTransferCodes_Users_InitiatedBy] FOREIGN KEY ([InitiatedBy])
        REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores one-time codes confirming organization ownership transfers.
-- The code is emailed to the prospective new owner (TargetUserId); the current
-- owner enters it, proving both parties consent. CodeHash is Argon2id of the
-- 6-digit code; AttemptCount tracks failed verifications (max 5); UsedAt is set
-- on successful redemption.

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OwnershipTransferCodes_OrganizationId]
ON [dbo].[OwnershipTransferCodes] ([OrganizationId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_OwnershipTransferCodes_ExpiresAt]
ON [dbo].[OwnershipTransferCodes] ([ExpiresAt])
WHERE [UsedAt] IS NULL;
GO
