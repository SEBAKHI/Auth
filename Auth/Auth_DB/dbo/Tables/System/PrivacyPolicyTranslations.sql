CREATE TABLE [dbo].[PrivacyPolicyTranslations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PrivacyPolicyTranslations_Id] DEFAULT NEWID(),
    [VersionId] UNIQUEIDENTIFIER NOT NULL,
    [LanguageCode] NVARCHAR(10) NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,      -- the policy document; {{token}} placeholders are
                                               -- interpolated from live config at read time
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

-- The privacy-policy document per (version, language). Content is data, not
-- code: it is authored in the console and served to the accounts app, so legal
-- wording changes need no deployment. Rows carry no personal data.
