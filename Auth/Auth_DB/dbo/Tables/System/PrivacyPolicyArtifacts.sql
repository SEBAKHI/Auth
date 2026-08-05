CREATE TABLE [dbo].[PrivacyPolicyArtifacts]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PrivacyPolicyArtifacts_Id] DEFAULT NEWID(),
    [VersionId] UNIQUEIDENTIFIER NOT NULL,
    [LanguageCode] NVARCHAR(10) NOT NULL,       -- the language this document is SERVED as
    [SourceLanguageCode] NVARCHAR(10) NOT NULL, -- the language its body was WRITTEN in; differs
                                                -- from LanguageCode on a disclosed fallback, and
                                                -- the document says so in the reader's language
    [Html] NVARCHAR(MAX) NOT NULL,              -- complete standalone document: own head, inline
                                                -- styles, no script, no bundle reference
    [ContentHash] CHAR(64) NOT NULL,            -- SHA-256 of Html; the ETag, and the value an
                                                -- acknowledgement record should cite as evidence
    [StyleHash] NVARCHAR(100) NOT NULL,         -- base64 SHA-256 of the inline stylesheet, sent as
                                                -- style-src 'sha256-...'. Stored rather than
                                                -- recomputed: an older artifact carries an older
                                                -- stylesheet, and today's hash would not match it
    [DisclosureJson] NVARCHAR(MAX) NOT NULL,    -- the values frozen into Html, so the console can
                                                -- report drift instead of silently re-rendering
    [RenderedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PrivacyPolicyArtifacts_RenderedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PrivacyPolicyArtifacts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_PrivacyPolicyArtifacts_VersionLanguage] UNIQUE ([VersionId], [LanguageCode]),
    CONSTRAINT [FK_PrivacyPolicyArtifacts_Version] FOREIGN KEY ([VersionId])
        REFERENCES [dbo].[PrivacyPolicyVersions] ([Id]) ON DELETE CASCADE
);
GO

-- What the public is actually served, rendered once when a version is published
-- and returned verbatim thereafter.
--
-- Separate from PrivacyPolicyTranslations on purpose. That table answers "which
-- languages have been written"; this one answers "which languages can be read",
-- and publishing makes every supported language readable by falling back to the
-- neutral document with a disclosed notice. Merging them would make every
-- language look translated the moment it became servable.
--
-- Rows are immutable once written and permanent: they are the evidence of what
-- a given user was shown on a given date. Never swept.
