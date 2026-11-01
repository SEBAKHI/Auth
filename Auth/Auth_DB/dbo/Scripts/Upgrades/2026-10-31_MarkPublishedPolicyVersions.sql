-- Mark every privacy-policy revision that was published as published once, on
-- EXISTING databases (UI batch P2, item 1).
--
-- PrivacyPolicyVersions.FirstPublishedAtUtc is what lets a draft be deleted and
-- keeps anything people were shown: a revision superseded by a later one goes
-- back to IsPublished = 0, so IsPublished alone cannot tell a draft from a former
-- policy. The column arrives NULL on every existing row. This statement fills it
-- for the revision published now and for every revision with served documents
-- (written only by a publish), dating it by its earliest served document, else by
-- its effective date.
--
-- A revision published before served documents were stored, and already
-- superseded, has neither mark; the delete endpoint also refuses any version that
-- account-deletion records cite, which covers a version that was in force.
--
-- Idempotent: it only fills NULLs, and a publish writes the column itself, so on a
-- fresh database and on every publish after the first it changes nothing.
--
-- sqlcmd-safe: pure ASCII, no BOM, QUOTED_IDENTIFIER ON, and its own GO at the end.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

UPDATE v
SET [FirstPublishedAtUtc] = COALESCE(
        (SELECT MIN(a.[RenderedAt]) FROM [dbo].[PrivacyPolicyArtifacts] a WHERE a.[VersionId] = v.[Id]),
        v.[EffectiveDateUtc])
FROM [dbo].[PrivacyPolicyVersions] v
WHERE v.[FirstPublishedAtUtc] IS NULL
  AND (v.[IsPublished] = 1
       OR EXISTS (SELECT 1 FROM [dbo].[PrivacyPolicyArtifacts] a WHERE a.[VersionId] = v.[Id]));

PRINT 'Published privacy policy revisions marked as published once';
GO
