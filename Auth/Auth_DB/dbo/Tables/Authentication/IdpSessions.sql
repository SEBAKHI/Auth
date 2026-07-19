CREATE TABLE [dbo].[IdpSessions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_IdpSessions_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR(500) NOT NULL,   -- HMAC-SHA256 hash of the cookie token; plain token is never stored
    [ExpiresAt] DATETIME2 NOT NULL,
    [RevokedAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_IdpSessions_CreatedAt] DEFAULT GETUTCDATE(),
    [IpAddress] NVARCHAR(45) NULL,
    [DeviceInfo] NVARCHAR(500) NULL,

    CONSTRAINT [PK_IdpSessions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_IdpSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Identity-provider (SSO) sessions: the server-side counterpart of the
-- HttpOnly IdP session cookie set on the auth origin at login. NOT the same as
-- UserSessions (per-application device sessions keyed by refresh-token hash):
-- one IdP session can spawn many UserSessions via authorization-code exchanges.

CREATE NONCLUSTERED INDEX [IX_IdpSessions_TokenHash]
ON [dbo].[IdpSessions] ([TokenHash])
WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_IdpSessions_UserId]
ON [dbo].[IdpSessions] ([UserId])
WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_IdpSessions_ExpiresAt]
ON [dbo].[IdpSessions] ([ExpiresAt]);
GO
