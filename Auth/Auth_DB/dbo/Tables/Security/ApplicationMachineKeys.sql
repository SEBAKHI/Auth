-- An application's machine credential (OI-128): the PUBLIC half of an ES256 key pair, with which the
-- application's own server signs the client assertion of the client_credentials grant
-- (private_key_jwt, RFC 7523). The private key is never stored here, and never logged: a key the
-- server generates is handed back once, in the response that created it.
--
-- Kid is the RFC 7638 thumbprint of the public key, so one key can belong to one application only.
-- Kid and the assertion's kid are compared byte for byte (BIN2), not under the database's
-- case-insensitive collation.
CREATE TABLE [dbo].[ApplicationMachineKeys]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationMachineKeys_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Kid] NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Name] NVARCHAR(200) NULL,
    [Algorithm] NVARCHAR(10) NOT NULL,      -- always 'ES256' today
    [PublicKeyJwk] NVARCHAR(1000) NOT NULL, -- the public JWK only: kty, crv, x, y
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationMachineKeys_CreatedAt] DEFAULT SYSUTCDATETIME(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [LastUsedAt] DATETIME2 NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [RevokeReason] NVARCHAR(200) NULL,

    CONSTRAINT [PK_ApplicationMachineKeys] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationMachineKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UX_ApplicationMachineKeys_Kid] ON [dbo].[ApplicationMachineKeys] ([Kid]);
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationMachineKeys_ApplicationId] ON [dbo].[ApplicationMachineKeys] ([ApplicationId]) WHERE [RevokedAt] IS NULL;
GO
