CREATE TABLE [dbo].[AuditLogs]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AuditLogs_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [SessionId] UNIQUEIDENTIFIER NULL,
    [Action] NVARCHAR(100) NOT NULL,
    [EntityType] NVARCHAR(100) NULL,
    [EntityId] UNIQUEIDENTIFIER NULL,
    [OldValues] NVARCHAR(MAX) NULL,
    [NewValues] NVARCHAR(MAX) NULL,
    [IpAddress] NVARCHAR(45) NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [Details] NVARCHAR(MAX) NULL,
    [Timestamp] DATETIME2 NOT NULL CONSTRAINT [DF_AuditLogs_Timestamp] DEFAULT GETUTCDATE(),
    [PerformedBy] UNIQUEIDENTIFIER NULL,

    -- Added after PerformedBy on purpose: appending keeps an existing production
    -- table alterable in place instead of forcing a rebuild on publish.
    --
    -- All four are NULLable, including IsSuccess, and that is the point rather
    -- than an oversight. The domain has carried these fields since the beginning
    -- while the table did not, so the read path filled them in from nowhere:
    -- ActionType came back as the literal 'System' and IsSuccess as true for
    -- every row ever written, which meant the audit screen showed every event as
    -- a success because it had been told to, not because it was one. A default
    -- of 1 here would carry that same claim forward onto rows written before the
    -- column existed. NULL says the honest thing about them: not recorded.
    [ActionType] NVARCHAR(50) NULL,
    [IsSuccess] BIT NULL,
    [ErrorMessage] NVARCHAR(1000) NULL,
    [CorrelationId] NVARCHAR(100) NULL,

    CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- Action examples: 'user.login', 'user.logout', 'user.created', 'user.updated',
--                  'user.deleted', 'password.changed', 'password.reset',
--                  'role.assigned', 'role.removed', 'permission.granted',
--                  'permission.revoked', '2fa.enabled', '2fa.disabled',
--                  'apikey.created', 'apikey.revoked', 'session.ended'
-- EntityType examples: 'User', 'Role', 'Permission', 'Application', 'ApiKey', 'Session'
-- OldValues and NewValues are JSON for tracking changes
-- PerformedBy may differ from UserId (e.g., admin changing user password)

-- Indexes
CREATE NONCLUSTERED INDEX [IX_AuditLogs_UserId]
ON [dbo].[AuditLogs] ([UserId], [Timestamp] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_Action]
ON [dbo].[AuditLogs] ([Action], [Timestamp] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_Timestamp]
ON [dbo].[AuditLogs] ([Timestamp] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_ApplicationId]
ON [dbo].[AuditLogs] ([ApplicationId], [Timestamp] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_EntityType_EntityId]
ON [dbo].[AuditLogs] ([EntityType], [EntityId]);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_PerformedBy]
ON [dbo].[AuditLogs] ([PerformedBy], [Timestamp] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_SessionId]
ON [dbo].[AuditLogs] ([SessionId]);
GO

-- Failures are the rows anyone investigating an incident opens first, and they
-- are a small fraction of the table, so the index covers only them. Rows written
-- before IsSuccess existed are NULL and stay out of it, which is correct: they
-- are not known failures.
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Failures]
ON [dbo].[AuditLogs] ([Timestamp] DESC)
INCLUDE ([Action], [UserId], [PerformedBy])
WHERE [IsSuccess] = 0;
GO

-- The twin of IX_AuditLogs_Action, for the category rather than the action. The
-- console can now narrow the audit table to one category, and without this the
-- server answers that by walking IX_AuditLogs_Timestamp newest-first and looking
-- up every candidate row until fifty of them match. AuditLogRetentionDays cannot
-- be set below 1095 days, so this table is measured in millions of rows and a
-- rare category makes that walk arbitrarily long. Same shape as the Action index
-- because the query is the same shape: equality on the column, ordered by
-- Timestamp DESC.
--
-- Publishing this on a populated production table builds the index, which takes
-- a table lock in Standard edition while audit rows are still being written on
-- the sign-in path. Deploy it in a maintenance window.
CREATE NONCLUSTERED INDEX [IX_AuditLogs_ActionType]
ON [dbo].[AuditLogs] ([ActionType], [Timestamp] DESC);
GO
