CREATE TABLE [dbo].[RefreshTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Token] NVARCHAR(500) NOT NULL,
    [TokenHash] NVARCHAR(500) NOT NULL,   -- Argon2id hash (longer than SHA256)
    [JwtId] NVARCHAR(100) NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [DeviceInfo] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(45) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [ReplacedByToken] NVARCHAR(500) NULL,
    [ReasonRevoked] NVARCHAR(200) NULL,

    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_RefreshTokens_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- TokenHash is Argon2id hash of Token for secure lookup
-- JwtId links to the 'jti' claim in the access token
-- ReplacedByToken for token rotation chain tracking

-- Indexes
-- Token index for plain text lookup (primary lookup method)
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_Token]
ON [dbo].[RefreshTokens] ([Token]);
GO

-- TokenHash index for Argon2id hash (kept for potential additional verification)
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash]
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
