CREATE TABLE [dbo].[AccountDeletionTombstones]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AccountDeletionTombstones_Id] DEFAULT NEWID(),
    [EmailHash] NVARCHAR(200) NOT NULL,        -- keyed HMAC-SHA256 of NormalizedEmail
    [DeletedAtUtc] DATETIME2 NOT NULL CONSTRAINT [DF_AccountDeletionTombstones_DeletedAtUtc] DEFAULT GETUTCDATE(),
    [PolicyVersion] NVARCHAR(20) NOT NULL,
    -- Which identifier HMAC key produced EmailHash. Without it the key can
    -- never be rotated: rows hashed under an old key are indistinguishable
    -- from rows hashed under a new one, so every reservation would silently
    -- stop matching with nothing failing anywhere.
    [KeyVersion] TINYINT NOT NULL CONSTRAINT [DF_AccountDeletionTombstones_KeyVersion] DEFAULT 1,

    CONSTRAINT [PK_AccountDeletionTombstones] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_AccountDeletionTombstones_EmailHash] UNIQUE ([EmailHash])
);
GO

-- Destruction registry: {keyed identifier digest, deleted-at, policy version,
-- key version} and nothing else. Registration paths check EmailHash, so a
-- deleted address cannot be re-registered while its reservation is live, and
-- restores re-apply deletion from this registry.
--
-- UsernameHash was removed: nothing ever read it. The reservation guard checks
-- the e-mail digest only, and the username is derived rather than chosen by the
-- user, so the column was a second permanent identifier of a person kept for a
-- check that did not exist.
--
-- Rows are NOT permanent. The retention sweep deletes them once the reservation
-- window (AccountDeletion:IdentifierReservationDays) has passed. That deletion
-- IS the erasure: a keyed digest of an e-mail address, under a key this system
-- keeps, is pseudonymised personal data — not anonymous — so it needs an end of
-- life like every other record. Destroying the key is not an available
-- substitute, because the key must stay readable for as long as any live
-- reservation depends on it.
--
-- The window is floored at the audit-log retention period: an address may only
-- be released once every record still keyed to it has expired.
-- AccountDeletionRetentionTests enforces that relationship.

CREATE NONCLUSTERED INDEX [IX_AccountDeletionTombstones_DeletedAtUtc]
ON [dbo].[AccountDeletionTombstones] ([DeletedAtUtc]);
GO
