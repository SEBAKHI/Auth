CREATE TABLE [dbo].[UserUiPreferences]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_UserUiPreferences_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Key] NVARCHAR(100) NOT NULL,
    [Value] NVARCHAR(4000) NOT NULL,
    [ModifiedAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserUiPreferences_ModifiedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_UserUiPreferences] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserUiPreferences_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_UserUiPreferences_UserKey] UNIQUE ([UserId], [Key])
);
GO

-- Per-user client display preferences, one row per key, so two browser tabs
-- editing different tables cannot overwrite each other's work. A single
-- JSON document per user would make every write a whole-document write.
--
-- Key: an allow-listed namespace, currently 'table:<tableId>'. The allow list
-- is enforced by the write validator, not here, because a CHECK constraint
-- would need a schema change every time the UI grows a new preference kind.
--
-- Value: JSON, validated and length-capped on write. The cap plus a per-user
-- key limit is what stops an authenticated caller turning this into free
-- storage; without both, the endpoint is an upload service.
--
-- The FK does not cascade, matching every other user-owned table: the hard
-- delete purge in UserRepository removes these rows explicitly, and
-- UserHardDeleteSqlTests fails the build if a new Users-referencing table is
-- added without extending it. One mechanism, and it is the tested one.

-- Indexes
-- The unique constraint above already indexes (UserId, Key), which serves the
-- only read this table has: fetch every preference for one user.
