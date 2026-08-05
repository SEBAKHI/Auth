-- ============================================================================
-- 2026-08-05 — Privacy policy becomes a publish-time artifact
--
-- Publishing used to be a flag flip: the public page fetched JSON at render
-- time and interpolated the controller identity in the browser. That made the
-- document's correctness depend on a network round-trip completing, and on the
-- bundled fallback that filled the gap — a fallback carrying bracketed
-- placeholders and a draft banner, i.e. exactly the text a privacy notice must
-- never show.
--
-- Publishing now renders each supported language once, with the controller
-- identity of that moment interpolated in, and stores the bytes. Reads return
-- those bytes and touch nothing else.
--
-- Idempotent; safe to run more than once.
-- ============================================================================

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.PrivacyPolicyArtifacts', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PrivacyPolicyArtifacts]
    (
        [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PrivacyPolicyArtifacts_Id] DEFAULT NEWID(),
        [VersionId] UNIQUEIDENTIFIER NOT NULL,
        [LanguageCode] NVARCHAR(10) NOT NULL,
        [SourceLanguageCode] NVARCHAR(10) NOT NULL,
        [Html] NVARCHAR(MAX) NOT NULL,
        [ContentHash] CHAR(64) NOT NULL,
        [DisclosureJson] NVARCHAR(MAX) NOT NULL,
        [RenderedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PrivacyPolicyArtifacts_RenderedAt] DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_PrivacyPolicyArtifacts] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_PrivacyPolicyArtifacts_VersionLanguage] UNIQUE ([VersionId], [LanguageCode]),
        CONSTRAINT [FK_PrivacyPolicyArtifacts_Version] FOREIGN KEY ([VersionId])
            REFERENCES [dbo].[PrivacyPolicyVersions] ([Id]) ON DELETE CASCADE
    );

    PRINT 'Created dbo.PrivacyPolicyArtifacts';
END
ELSE
BEGIN
    PRINT 'dbo.PrivacyPolicyArtifacts already exists';
END
GO

-- No backfill here on purpose.
--
-- Only the application can render an artifact: it holds the template, the law
-- links and the DataController settings. Reproducing that in T-SQL would create
-- a second renderer whose output could differ from the one that publishes,
-- which is precisely the divergence this change exists to remove.
--
-- Until the currently published version is re-published, the read path serves
-- the pre-artifact JSON contract, exactly as before this deployment.
PRINT 'Re-publish the current policy version from the console to generate its artifacts.';
GO
