CREATE TABLE [dbo].[NotificationTemplateVersions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationTemplateVersions_Id] DEFAULT NEWID(),
    [TemplateId] UNIQUEIDENTIFIER NOT NULL,
    [VersionNumber] INT NOT NULL,
    [ChangeNote] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationTemplateVersions_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_NotificationTemplateVersions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationTemplateVersions_Templates] FOREIGN KEY ([TemplateId]) REFERENCES [dbo].[NotificationTemplates]([Id]),
    CONSTRAINT [UQ_NotificationTemplateVersions_Template_Version] UNIQUE ([TemplateId], [VersionNumber])
);
GO
