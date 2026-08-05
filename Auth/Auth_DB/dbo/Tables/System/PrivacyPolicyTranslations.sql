CREATE TABLE [dbo].[PrivacyPolicyTranslations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PrivacyPolicyTranslations_Id] DEFAULT NEWID(),
    [VersionId] UNIQUEIDENTIFIER NOT NULL,
    [LanguageCode] NVARCHAR(10) NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,      -- the authored document; {{token}} placeholders are
                                               -- interpolated when the version is PUBLISHED, into
                                               -- RenderedHtml below — never on the read path
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PrivacyPolicyTranslations_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_PrivacyPolicyTranslations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_PrivacyPolicyTranslations_VersionLanguage] UNIQUE ([VersionId], [LanguageCode]),
    CONSTRAINT [FK_PrivacyPolicyTranslations_Version] FOREIGN KEY ([VersionId])
        REFERENCES [dbo].[PrivacyPolicyVersions] ([Id]) ON DELETE CASCADE
);
GO

-- The AUTHORED privacy-policy document per (version, language). Content is
-- data, not code: it is written in the console, so legal wording changes need
-- no deployment. Rows carry no personal data.
--
-- A row here means "this language has been written". What the public is served
-- lives in PrivacyPolicyArtifacts, produced at publish time — keeping the two
-- apart is what lets the console still say which languages are actually
-- translated after every language has become servable.
