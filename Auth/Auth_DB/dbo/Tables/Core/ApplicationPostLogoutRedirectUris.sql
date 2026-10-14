CREATE TABLE [dbo].[ApplicationPostLogoutRedirectUris]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationPostLogoutRedirectUris_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Uri] NVARCHAR(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationPostLogoutRedirectUris_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_ApplicationPostLogoutRedirectUris] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationPostLogoutRedirectUris_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_ApplicationPostLogoutRedirectUris_ApplicationId_Uri] UNIQUE ([ApplicationId], [Uri])
);
GO

-- Exact-match allowlist of the addresses each application may have the
-- browser sent back to after an RP-initiated logout (post_logout_redirect_uri,
-- OpenID Connect RP-Initiated Logout 1.0). The end-session endpoint only ever
-- redirects to a URI stored here, for the application named by client_id.
-- Kept apart from ApplicationRedirectUris: a sign-in return and a sign-out
-- return are different permissions.
-- Uri is compared byte for byte (BIN2), as the code matches it (ordinal): two
-- addresses that differ only in letter case are two entries, and the unique key
-- must not refuse the second one under the database's case-insensitive collation.

CREATE NONCLUSTERED INDEX [IX_ApplicationPostLogoutRedirectUris_ApplicationId]
ON [dbo].[ApplicationPostLogoutRedirectUris] ([ApplicationId]);
GO
