CREATE TABLE [dbo].[UserPasskeys]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_UserPasskeys_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CredentialId] VARBINARY(1023) NOT NULL,   -- the WebAuthn credential id, compared as bytes (never base64 text under a case-insensitive collation)
    [PublicKey] VARBINARY(MAX) NOT NULL,       -- the COSE public key: not a secret
    [SignCount] BIGINT NOT NULL CONSTRAINT [DF_UserPasskeys_SignCount] DEFAULT 0,  -- the authenticator's signature counter last accepted (an unsigned 32-bit value)
    [RpId] NVARCHAR(253) NOT NULL,             -- the RP ID it is bound to (Passkeys:RpId at registration); one of another RP ID neither signs in nor counts
    [IsUserVerified] BIT NOT NULL,             -- the device verified the person at registration: always 1 (registration requires it)
    [IsBackupEligible] BIT NOT NULL CONSTRAINT [DF_UserPasskeys_IsBackupEligible] DEFAULT 0,  -- BE flag: may be synced to the owner's other devices
    [IsBackedUp] BIT NOT NULL CONSTRAINT [DF_UserPasskeys_IsBackedUp] DEFAULT 0,              -- BS flag: synced when last seen
    [Name] NVARCHAR(128) NOT NULL,             -- the owner's name for it: at most 64 code points, which can take 128 UTF-16 units
    [CreatedAt] DATETIME2 NOT NULL,
    [LastUsedAt] DATETIME2 NULL,
    [LastCounterRegressionAt] DATETIME2 NULL,  -- when the counter last went backwards (a copied or broken authenticator)
    [RevokedAt] DATETIME2 NULL,                -- NULL while it is live; a passkey is never deleted while its account lives
    [RevokedReason] NVARCHAR(32) NULL,         -- PasskeyRemovalReasons: user, peer-reset, password-reset, password-change, admin-lock, rp-id-move, break-glass
    [RevokedBy] UNIQUEIDENTIFIER NULL,         -- the person who removed it; NULL for a script

    CONSTRAINT [PK_UserPasskeys] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserPasskeys_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- The passkeys of an account (S20, PK17): WebAuthn credentials its devices made and keep.
-- The private key never leaves the device. A credential id is unique across every account:
-- an authenticator never makes the same one twice, so a duplicate is refused.

CREATE UNIQUE NONCLUSTERED INDEX [UX_UserPasskeys_CredentialId]
ON [dbo].[UserPasskeys] ([CredentialId]);
GO

-- The live passkeys of an account under an RP ID: the list, the count, the limit of ten,
-- and the qualifying-factor check (FA2) every MFA policy reads.
CREATE NONCLUSTERED INDEX [IX_UserPasskeys_Live]
ON [dbo].[UserPasskeys] ([UserId], [RpId])
INCLUDE ([IsUserVerified], [CreatedAt])
WHERE [RevokedAt] IS NULL;
GO

-- The live passkeys by RP ID alone: PK3's check that none is bound to another RP ID runs on
-- every passkey request, before authentication, and with this index its "<> @RpId" is two
-- range seeks instead of a read of every live row (REVIEW 150 F5).
CREATE NONCLUSTERED INDEX [IX_UserPasskeys_LiveRpId]
ON [dbo].[UserPasskeys] ([RpId])
WHERE [RevokedAt] IS NULL;
GO
