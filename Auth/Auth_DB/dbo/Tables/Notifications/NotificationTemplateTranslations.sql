CREATE TABLE [dbo].[NotificationTemplateTranslations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationTemplateTranslations_Id] DEFAULT NEWID(),
    [VersionId] UNIQUEIDENTIFIER NOT NULL,
    -- Language code: 'en', 'ar', 'tr', 'fr', 'zh', 'ur', 'fa'.
    [LanguageCode] NVARCHAR(10) NOT NULL,
    -- Subject and bodies are Liquid templates rendered with the type's variables.
    [Subject] NVARCHAR(500) NOT NULL,
    [BodyHtml] NVARCHAR(MAX) NOT NULL,
    -- NULL = derive the plain-text alternative from BodyHtml at render time.
    [BodyText] NVARCHAR(MAX) NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_NotificationTemplateTranslations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationTemplateTranslations_Versions] FOREIGN KEY ([VersionId]) REFERENCES [dbo].[NotificationTemplateVersions]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_NotificationTemplateTranslations_Version_Language] UNIQUE ([VersionId], [LanguageCode])
);
GO
