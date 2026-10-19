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
    [AuthMethods] INT NULL,               -- P3 deploy-1 batch: the authentication methods this IdP session proved; NULL = recorded before the column existed (S08)
    [IsPersistent] BIT NULL,              -- OI-154: 1 = "Stay signed in" (persistent cookie, no idle limit); 0 or NULL = a browser session, which idles out
    [LastActivityAt] DATETIME2 NULL,      -- OI-154: last use (a sign-in through authorize, or a refresh of a token born from it); NULL = none since CreatedAt
    [DeviceId] NVARCHAR(64) NULL,         -- OI-178: the client-supplied device identifier the sign-in sent (X-Device-Id); authorize copies it onto the codes it issues, so a server-side application's sign-in is filed under the person's browser. Forgeable, never an authorization input. NULL = none sent, or a session recorded before the column

    CONSTRAINT [PK_IdpSessions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_IdpSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Identity-provider (SSO) sessions: the server-side counterpart of the
-- HttpOnly IdP session cookie set on the auth origin at login. NOT the same as
-- UserSessions (per-application device sessions keyed by refresh-token hash):
-- one IdP session can spawn many UserSessions via authorization-code exchanges.

-- Not filtered (OI-155): a sign-out also looks up a session a read already
-- revoked as idle, so the sign-ins born from it still end. The live-only read
-- every sign-in uses seeks on it too.
CREATE NONCLUSTERED INDEX [IX_IdpSessions_TokenHash]
ON [dbo].[IdpSessions] ([TokenHash]);
GO

CREATE NONCLUSTERED INDEX [IX_IdpSessions_UserId]
ON [dbo].[IdpSessions] ([UserId])
WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_IdpSessions_ExpiresAt]
ON [dbo].[IdpSessions] ([ExpiresAt]);
GO

-- Serves the revoked half of the retention sweep, which runs as two statements
-- rather than one OR so each half lands on an index: the expiry half uses
-- IX_IdpSessions_ExpiresAt above, this one covers the revoked half.
CREATE NONCLUSTERED INDEX [IX_IdpSessions_RevokedAt]
ON [dbo].[IdpSessions] ([RevokedAt])
WHERE [RevokedAt] IS NOT NULL;
GO
