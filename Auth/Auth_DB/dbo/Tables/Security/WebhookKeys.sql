CREATE TABLE [dbo].[WebhookKeys]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WebhookKeys_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [KeyPrefix] NVARCHAR(10) NOT NULL,      -- First chars for identification (e.g., 'wk_prod_')
    [KeyHash] NVARCHAR(500) NOT NULL,       -- HMAC-SHA256 hash of the full key (deterministic)
    [TargetUrl] NVARCHAR(2000) NOT NULL,    -- The webhook endpoint URL this key authenticates for
    [Environment] NVARCHAR(20) NOT NULL CONSTRAINT [DF_WebhookKeys_Environment] DEFAULT 'production',  -- 'production', 'staging', 'development'
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_WebhookKeys_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [RevokeReason] NVARCHAR(200) NULL,

    CONSTRAINT [PK_WebhookKeys] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_WebhookKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- Index for deterministic hash lookup (HMAC-SHA256 allows direct lookup by hash)
CREATE UNIQUE NONCLUSTERED INDEX [IX_WebhookKeys_KeyHash] ON [dbo].[WebhookKeys] ([KeyHash]) WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_WebhookKeys_ApplicationId] ON [dbo].[WebhookKeys] ([ApplicationId]) WHERE [RevokedAt] IS NULL;
GO
