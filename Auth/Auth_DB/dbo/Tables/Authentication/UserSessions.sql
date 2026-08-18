CREATE TABLE [dbo].[UserSessions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_UserSessions_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [SessionToken] NVARCHAR(500) NOT NULL,
    [IpAddress] NVARCHAR(45) NOT NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [DeviceType] NVARCHAR(50) NULL,
    [Location] NVARCHAR(200) NULL,
    [StartedAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserSessions_StartedAt] DEFAULT GETUTCDATE(),
    [LastActivityAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserSessions_LastActivityAt] DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,
    [EndedAt] DATETIME2 NULL,
    [EndReason] NVARCHAR(100) NULL,
    [DeviceName] NVARCHAR(100) NULL,
    [DeviceId] NVARCHAR(64) NULL,
    [DeviceHash] CHAR(64) NULL,

    CONSTRAINT [PK_UserSessions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserSessions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- EndReason: written by the code, not constrained. Current writers are 'logout',
-- 'User terminated', 'User terminated all sessions', 'User terminated all other
-- sessions', 'Password changed', 'device_forgotten', 'session_limit', 'Account
-- deleted' and "Account locked: {reason}". 'timeout' is written by the daily
-- retention sweep (ExpiredDataCleanupWorker), which stamps rows that passed
-- ExpiresAt without being ended by anything else. It backdates EndedAt to
-- ExpiresAt rather than to the sweep time, so the history says when the
-- session actually lapsed. Unstamped rows were already invisible to the
-- active-session filter; the sweep changes what the record says, not who is
-- signed in. This table is never emptied — a session is history a user can
-- see, so it is corrected rather than deleted.
--
-- 'session_limit': the account was over Session:MaxConcurrentSessions and this
-- was one of its least recently used sessions. Written by
-- TerminateBeyondLimitAsync, which ranks by LastActivityAt DESC and ends
-- everything past the limit in one statement — so the reason marks a session
-- the user did not end and was not warned about beforehand, which is why the
-- eviction also sends them mail.
--
-- DeviceType values: 'desktop', 'mobile', 'tablet', 'unknown'. The form factor,
-- parsed from the user agent — NOT a reference to a UserKnownDevices row. The
-- two answer different questions: "is this a phone?" and "have I seen this
-- browser before?".
--
-- DeviceName: the human label, e.g. "Chrome on Windows". Parsed server-side by
-- UserAgentParser so this column and the new-device email cannot disagree.
--
-- DeviceId: the client-supplied per-browser identifier. NULL for clients with no
-- browser storage to keep it in, such as the OAuth token endpoint. Forgeable, and
-- never read as an authorization input.
--
-- DeviceHash: the UserKnownDevices signature for the browser this session came
-- from — SHA-256 over the same (DeviceId, browser family, OS family) material, so
-- the join is a key match rather than a guess. Denormalised rather than a foreign
-- key: the ledger row is written on a path allowed to fail, can lose an insert
-- race, and may later be forgotten by the user; a constraint would turn any of
-- those into a failed sign-in or a deleted session record. NULL means the session
-- cannot be attributed to a browser.

-- Indexes
CREATE NONCLUSTERED INDEX [IX_UserSessions_UserId]
ON [dbo].[UserSessions] ([UserId])
WHERE [EndedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_UserSessions_SessionToken]
ON [dbo].[UserSessions] ([SessionToken]);
GO

CREATE NONCLUSTERED INDEX [IX_UserSessions_ExpiresAt]
ON [dbo].[UserSessions] ([ExpiresAt])
WHERE [EndedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_UserSessions_ApplicationId]
ON [dbo].[UserSessions] ([ApplicationId])
WHERE [EndedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_UserSessions_LastActivityAt]
ON [dbo].[UserSessions] ([LastActivityAt] DESC)
WHERE [EndedAt] IS NULL;
GO

-- Serves the forget-browser cascade: find every live session started from one
-- browser so they can be terminated with the ledger row that names it.
CREATE NONCLUSTERED INDEX [IX_UserSessions_DeviceHash]
ON [dbo].[UserSessions] ([UserId], [DeviceHash])
WHERE [EndedAt] IS NULL;
GO
