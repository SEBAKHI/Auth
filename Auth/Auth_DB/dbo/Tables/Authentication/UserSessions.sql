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

    CONSTRAINT [PK_UserSessions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserSessions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- EndReason values: 'logout', 'timeout', 'forced', 'security', 'password_changed'
-- DeviceType values: 'desktop', 'mobile', 'tablet', 'unknown'

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
