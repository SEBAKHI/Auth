CREATE TABLE [dbo].[UploadedImages]
(
    [StorageKey] NVARCHAR(100) NOT NULL,        -- the {guid:N}.webp the storage service minted
    [UploadedBy] UNIQUEIDENTIFIER NOT NULL,     -- who put the bytes on disk, not who ends up displaying them
    [SizeBytes] BIGINT NOT NULL,                -- of the re-encoded file, which is what actually occupies the volume
    [UploadedAt] DATETIME2 NOT NULL CONSTRAINT [DF_UploadedImages_UploadedAt] DEFAULT GETUTCDATE(),
    [AttachedAt] DATETIME2 NULL,                -- NULL means nothing points at it yet

    CONSTRAINT [PK_UploadedImages] PRIMARY KEY CLUSTERED ([StorageKey])
);
GO

-- Ledger for the uploads volume, answering three questions the filesystem cannot.
--
-- HOW MUCH HAS THIS PERSON STORED. Uploading had no per-user limit, only a
-- per-file one, so any authenticated user could fill the volume one four-megabyte
-- request at a time. On shared hosting a full volume stops the whole tenant, not
-- just the uploads.
--
-- IS ANYTHING STILL USING THIS FILE. Upload and attach are separate calls, so a
-- file whose upload succeeded and whose form was then abandoned stayed on disk
-- with nothing referencing it and nothing ever looking for it.
--
-- WHOSE FILE IS THIS. The upload returns a key and the attach call takes one, so
-- possession of a key was the whole of the claim to it. Setting a profile image
-- to somebody else's key and then changing it again deleted their file: the
-- replace path removes the key it is replacing, and that key had been accepted
-- from the client without asking whose it was.

CREATE NONCLUSTERED INDEX [IX_UploadedImages_UploadedBy]
ON [dbo].[UploadedImages] ([UploadedBy])
INCLUDE ([SizeBytes]);
GO

-- The reclamation sweep reads only unattached rows, and they are the small
-- minority, so the index carries only them.
CREATE NONCLUSTERED INDEX [IX_UploadedImages_Unattached]
ON [dbo].[UploadedImages] ([UploadedAt])
WHERE [AttachedAt] IS NULL;
GO
