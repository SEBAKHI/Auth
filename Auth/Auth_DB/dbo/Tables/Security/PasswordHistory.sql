CREATE TABLE [dbo].[PasswordHistory]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PasswordHistory_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PasswordHash] NVARCHAR(500) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PasswordHistory_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PasswordHistory] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PasswordHistory_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Stores previous password hashes to prevent password reuse
-- Default policy: Prevent reuse of last 12 passwords //TODO: Make it last 3 passwords only
-- PasswordHash uses same Argon2id format as Users table

-- Indexes
CREATE NONCLUSTERED INDEX [IX_PasswordHistory_UserId]
ON [dbo].[PasswordHistory] ([UserId], [CreatedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordHistory_CreatedAt]
ON [dbo].[PasswordHistory] ([CreatedAt] DESC);
GO
