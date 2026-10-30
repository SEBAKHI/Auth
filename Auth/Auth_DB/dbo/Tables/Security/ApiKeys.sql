CREATE TABLE [dbo].[ApiKeys]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApiKeys_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [KeyPrefix] NVARCHAR(10) NOT NULL,      -- First chars for identification (e.g., 'ak_prod_')
    [KeyHash] NVARCHAR(500) NOT NULL,       -- Argon2id hash of the full key
    [Environment] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ApiKeys_Environment] DEFAULT 'production',  -- 'production', 'staging', 'development'
    [RateLimitPerMinute] INT NOT NULL CONSTRAINT [DF_ApiKeys_RateLimitPerMinute] DEFAULT 60,
    [RateLimitPerDay] INT NOT NULL CONSTRAINT [DF_ApiKeys_RateLimitPerDay] DEFAULT 10000,
    [AllowedIps] NVARCHAR(MAX) NULL,        -- JSON array of allowed IPs (NULL = all)
    [AllowedOrigins] NVARCHAR(MAX) NULL,    -- JSON array of allowed CORS origins
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApiKeys_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [RevokeReason] NVARCHAR(200) NULL,
    -- OI-128 PR B: the random selector inside every key minted since, stored in clear for the
    -- one-row lookup before the one hash verify. NULL for an older key, which no longer
    -- validates. Last, so a deploy only adds it. Binary collation: the selector is base62, so
    -- case is meaning, for the lookup and for the unique index alike.
    [KeySelector] NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,

    CONSTRAINT [PK_ApiKeys] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApiKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- Indexes for efficient lookups
CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyHash] ON [dbo].[ApiKeys] ([KeyHash]) WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_ApiKeys_ApplicationId] ON [dbo].[ApiKeys] ([ApplicationId]) WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyPrefix] ON [dbo].[ApiKeys] ([KeyPrefix]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UX_ApiKeys_KeySelector] ON [dbo].[ApiKeys] ([KeySelector]) WHERE [KeySelector] IS NOT NULL;
GO
