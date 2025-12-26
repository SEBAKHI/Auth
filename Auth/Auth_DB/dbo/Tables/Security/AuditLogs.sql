CREATE TABLE [dbo].[AuditLogs]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
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
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [PerformedBy] UNIQUEIDENTIFIER NULL,

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
