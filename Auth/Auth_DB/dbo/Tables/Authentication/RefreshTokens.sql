CREATE TABLE [dbo].[RefreshTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_RefreshTokens_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(100) NOT NULL,   -- HMAC-SHA256 hash (base64 encoded, ~44 chars)
    [JwtId] NVARCHAR(100) NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [DeviceInfo] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(45) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_RefreshTokens_CreatedAt] DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [ReplacedByTokenHash] NVARCHAR(100) NULL,  -- Hash of the replacement token (for rotation tracking)
    [ReasonRevoked] NVARCHAR(200) NULL,

    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_RefreshTokens_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- TokenHash is HMAC-SHA256 hash of the plain refresh token
-- The plain token is NEVER stored in the database for security
-- JwtId links to the 'jti' claim in the access token
-- ReplacedByTokenHash tracks the hash of the replacement token for rotation chain tracking

-- Indexes
-- TokenHash unique index for deterministic lookup (HMAC-SHA256 is deterministic)
CREATE UNIQUE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash]
ON [dbo].[RefreshTokens] ([TokenHash]);
GO

CREATE NONCLUSTERED INDEX [IX_RefreshTokens_UserId]
ON [dbo].[RefreshTokens] ([UserId]);
GO

CREATE NONCLUSTERED INDEX [IX_RefreshTokens_ExpiresAt]
ON [dbo].[RefreshTokens] ([ExpiresAt])
WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_RefreshTokens_JwtId]
ON [dbo].[RefreshTokens] ([JwtId]);
GO

CREATE NONCLUSTERED INDEX [IX_RefreshTokens_ApplicationId]
ON [dbo].[RefreshTokens] ([ApplicationId])
WHERE [RevokedAt] IS NULL;
GO
