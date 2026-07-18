CREATE TABLE [dbo].[NotificationTemplates]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationTemplates_Id] DEFAULT NEWID(),
    [NotificationTypeId] UNIQUEIDENTIFIER NOT NULL,
    -- NULL = the global fallback template; a value = an application-specific override.
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    -- 1 = Email, 2 = Sms, 3 = Push (NotificationChannelType enum).
    [Channel] TINYINT NOT NULL CONSTRAINT [DF_NotificationTemplates_Channel] DEFAULT 1,
    -- Language whose translation is mandatory and used as the last-resort content fallback.
    [DefaultLanguage] NVARCHAR(10) NOT NULL CONSTRAINT [DF_NotificationTemplates_DefaultLanguage] DEFAULT N'en',
    -- Pointer model: publish/rollback repoints PublishedVersionId in a single-row UPDATE,
    -- so all translations of a version go live (or roll back) atomically together.
    [PublishedVersionId] UNIQUEIDENTIFIER NULL,
    [DraftVersionId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationTemplates_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationTemplates_NotificationTypes] FOREIGN KEY ([NotificationTypeId]) REFERENCES [dbo].[NotificationTypes]([Id]),
    CONSTRAINT [FK_NotificationTemplates_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_NotificationTemplates_PublishedVersion] FOREIGN KEY ([PublishedVersionId]) REFERENCES [dbo].[NotificationTemplateVersions]([Id]),
    CONSTRAINT [FK_NotificationTemplates_DraftVersion] FOREIGN KEY ([DraftVersionId]) REFERENCES [dbo].[NotificationTemplateVersions]([Id]),
    CONSTRAINT [CK_NotificationTemplates_Channel] CHECK ([Channel] IN (1, 2, 3)),
    -- One template per (application, type, channel) scope; SQL Server treats NULLs as equal
    -- in unique constraints, so exactly one global template per (type, channel) is allowed.
    CONSTRAINT [UQ_NotificationTemplates_Resolution] UNIQUE ([ApplicationId], [NotificationTypeId], [Channel])
);
GO

-- Covering index for the send-path resolution query (type + channel, app-specific then global).
CREATE NONCLUSTERED INDEX [IX_NotificationTemplates_Resolution]
ON [dbo].[NotificationTemplates] ([NotificationTypeId], [Channel])
INCLUDE ([ApplicationId], [PublishedVersionId], [DefaultLanguage]);
GO
