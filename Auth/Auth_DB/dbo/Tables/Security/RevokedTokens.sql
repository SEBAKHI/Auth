CREATE TABLE [dbo].[RevokedTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_RevokedTokens_Id] DEFAULT NEWID(),
    [RevocationType] TINYINT NOT NULL,          -- 1 = Token (jti), 2 = Session (sid), 3 = User (all tokens before EffectiveAt)
    [RevocationKey] NVARCHAR(200) NOT NULL,     -- jti, sid, or userId as string
    [EffectiveAt] DATETIME2 NOT NULL,           -- User type: reject tokens issued at/before this; Token/Session: creation time
    [ExpiresAt] DATETIME2 NOT NULL,             -- entry may be purged after this (the revoked token/session can no longer be valid)
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_RevokedTokens_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_RevokedTokens] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Durable backing store for the token/session revocation blacklist.
-- The blacklist is held in memory for per-request checks; this table makes it
-- survive app-pool recycles (which are frequent on IIS/shared hosting) so a
-- logout or session termination keeps rejecting the still-valid access token
-- until it would have expired, instead of the in-memory list being wiped.

CREATE NONCLUSTERED INDEX [IX_RevokedTokens_ExpiresAt]
ON [dbo].[RevokedTokens] ([ExpiresAt]);
GO

CREATE NONCLUSTERED INDEX [IX_RevokedTokens_Type_Key]
ON [dbo].[RevokedTokens] ([RevocationType], [RevocationKey]);
GO
