CREATE TABLE [dbo].[ApiKeyScopes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [ApiKeyId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_ApiKeyScopes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApiKeyScopes_ApiKeys] FOREIGN KEY ([ApiKeyId]) REFERENCES [dbo].[ApiKeys]([Id]),
    CONSTRAINT [FK_ApiKeyScopes_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_ApiKeyScopes] UNIQUE ([ApiKeyId], [PermissionId])
);
GO

-- Index for efficient lookups
CREATE NONCLUSTERED INDEX [IX_ApiKeyScopes_ApiKeyId] ON [dbo].[ApiKeyScopes] ([ApiKeyId]);
GO
