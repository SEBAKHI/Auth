CREATE TABLE [dbo].[ApplicationConfidentialRedirectUris]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationConfidentialRedirectUris_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Uri] NVARCHAR(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationConfidentialRedirectUris_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_ApplicationConfidentialRedirectUris] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationConfidentialRedirectUris_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_ApplicationConfidentialRedirectUris_ApplicationId_Uri] UNIQUE ([ApplicationId], [Uri])
);
GO

-- OI-171: the server-side (confidential) redirect URIs of each application. A
-- code issued to one of these is redeemable only by a request that authenticates
-- the application with its sign-in key (private_key_jwt), and the refresh tokens
-- it yields are bound to that key. Same application, same client_id, same roles
-- and audience as the public list in ApplicationRedirectUris.
-- A table of its own, not a kind column on ApplicationRedirectUris, on purpose:
-- an API older than this table never reads these rows, so it refuses these
-- addresses at authorize instead of issuing a public code for them (fail closed).
-- Uri is compared byte for byte (BIN2), as the code matches it (ordinal).

CREATE NONCLUSTERED INDEX [IX_ApplicationConfidentialRedirectUris_ApplicationId]
ON [dbo].[ApplicationConfidentialRedirectUris] ([ApplicationId]);
GO
