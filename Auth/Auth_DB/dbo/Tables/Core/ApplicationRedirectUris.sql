CREATE TABLE [dbo].[ApplicationRedirectUris]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationRedirectUris_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Uri] NVARCHAR(500) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationRedirectUris_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_ApplicationRedirectUris] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationRedirectUris_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_ApplicationRedirectUris_ApplicationId_Uri] UNIQUE ([ApplicationId], [Uri])
);
GO

-- Exact-match allowlist of OAuth redirect URIs per application.
-- The authorize endpoint only ever redirects to a URI stored here
-- (RFC 6749 + OAuth 2.0 Security BCP: no wildcard/prefix matching).

CREATE NONCLUSTERED INDEX [IX_ApplicationRedirectUris_ApplicationId]
ON [dbo].[ApplicationRedirectUris] ([ApplicationId]);
GO
