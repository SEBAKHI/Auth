CREATE TABLE [dbo].[AccountDeletionTombstones]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AccountDeletionTombstones_Id] DEFAULT NEWID(),
    [EmailHash] NVARCHAR(200) NOT NULL,        -- HMAC-SHA256 of NormalizedEmail
    [UsernameHash] NVARCHAR(200) NOT NULL,     -- HMAC-SHA256 of UPPER(Username)
    [DeletedAtUtc] DATETIME2 NOT NULL CONSTRAINT [DF_AccountDeletionTombstones_DeletedAtUtc] DEFAULT GETUTCDATE(),
    [PolicyVersion] NVARCHAR(20) NOT NULL,

    CONSTRAINT [PK_AccountDeletionTombstones] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_AccountDeletionTombstones_EmailHash] UNIQUE ([EmailHash])
);
GO

-- Zero-PII destruction registry: {hashed identifier, deleted-at, policy version}
-- and nothing else. Rows are permanent — never swept. Registration paths check
-- both hashes so deleted identifiers are never recycled, and restores re-apply
-- deletion from this registry.

CREATE NONCLUSTERED INDEX [IX_AccountDeletionTombstones_UsernameHash]
ON [dbo].[AccountDeletionTombstones] ([UsernameHash]);
GO
