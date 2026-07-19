CREATE TABLE [dbo].[AuthorizationCodes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AuthorizationCodes_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CodeHash] NVARCHAR(500) NOT NULL,     -- HMAC-SHA256 hash (base64, ~44 chars); plain code is never stored
    [RedirectUri] NVARCHAR(500) NOT NULL,  -- exact URI the code was issued for; token request must match it
    [CodeChallenge] NVARCHAR(128) NOT NULL, -- PKCE S256 challenge (base64url SHA-256 of the verifier)
    [ExpiresAt] DATETIME2 NOT NULL,
    [ConsumedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_AuthorizationCodes_CreatedAt] DEFAULT GETUTCDATE(),
    [IpAddress] NVARCHAR(45) NULL,

    CONSTRAINT [PK_AuthorizationCodes] PRIMARY KEY CLUSTERED ([Id]),
    -- Codes are ephemeral (<=60s) throwaway artifacts scoped to the client, so
    -- they cascade away when the application is deleted (no ON DELETE on the
    -- Users FK: user deletion is guarded/handled separately, and two cascade
    -- paths to the same table are disallowed by SQL Server anyway).
    CONSTRAINT [FK_AuthorizationCodes_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AuthorizationCodes_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- One-time OAuth authorization codes (authorization-code + PKCE flow).
-- Lifetime is short (<= 60s, IdentityProvider:AuthorizationCodeLifetimeSeconds).
-- ConsumedAt is set atomically on redemption; a consumed code can never be
-- redeemed again (reuse attempts are rejected and logged).

CREATE NONCLUSTERED INDEX [IX_AuthorizationCodes_CodeHash]
ON [dbo].[AuthorizationCodes] ([CodeHash])
WHERE [ConsumedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_AuthorizationCodes_ExpiresAt]
ON [dbo].[AuthorizationCodes] ([ExpiresAt]);
GO
