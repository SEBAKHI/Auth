CREATE TABLE [dbo].[ApplicationPostLogoutRedirectUris]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationPostLogoutRedirectUris_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Uri] NVARCHAR(500) NOT NULL,
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

CREATE NONCLUSTERED INDEX [IX_ApplicationPostLogoutRedirectUris_ApplicationId]
ON [dbo].[ApplicationPostLogoutRedirectUris] ([ApplicationId]);
GO
