-- ============================================================================
-- 2026-10-06 — Lowercase the role codes stored while roles were upper-cased (OI-73)
--
-- MANUAL. Run by the operator once, after the API that stores role codes
-- lowercase is deployed, and only after telling every relying party when it
-- will run. Deliberately NOT in the post-deployment chain: a role code travels
-- in the `roles` claim of every token, and a relying party may compare it by
-- exact case (IsInRole and [Authorize(Roles = "...")] compare ordinally), so a
-- renamed code can silently switch off a role check in someone else's
-- application. When that happens is each operator's decision, never a side
-- effect of an upgrade.
--
-- Why rows can be uppercase: until that API, Role.Create upper-cased every new
-- code (send `editor`, store `EDITOR`), while the seeds insert their roles
-- lowercase (`admin`, `org-owner`, ...). New roles are now stored lowercase;
-- this script brings the older ones in line.
--
-- What it does: sets Code = LOWER(Code) on every role whose code is not
-- already lowercase. Every role about to change is SELECTed first (Id,
-- ApplicationId, the application's code, the old and the new code): send that
-- list to the relying parties, and keep it to undo any row on its own. The
-- comparison is binary (Latin1_General_BIN2) because the column's collation
-- ignores case, so a plain <> would find nothing. LOWER runs under that
-- collation too, never the database's: under a Turkish collation it turns "I"
-- into a dotless "ı", the bug Role.Create avoids with ToLowerInvariant.
--
-- Skipped and listed in a second result: a role whose lowercase code another
-- role of the same scope (the same application, or both platform roles)
-- already has or would get. That can happen only on a case-sensitive
-- database, where the rename would break UQ_Roles_Code_Application. Resolve
-- those by hand.
--
-- Idempotent: a second run lists nothing to change and changes nothing.
-- Undo, one printed row: UPDATE [dbo].[Roles] SET [Code] = N'<OldCode>' WHERE [Id] = '<Id>';
-- Tokens issued before the run keep the old codes until they expire.
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @Candidates TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [OldCode] NVARCHAR(100) NOT NULL,
    [NewCode] NVARCHAR(100) NOT NULL,
    [Collides] BIT NOT NULL
);

BEGIN TRANSACTION;

-- The roles whose code is not lowercase. The rows are held until the end, so
-- the list printed below is exactly the list changed, and no role can be
-- created or renamed between the read and the update.
INSERT INTO @Candidates ([Id], [ApplicationId], [OldCode], [NewCode], [Collides])
SELECT r.[Id], r.[ApplicationId], r.[Code], LOWER(r.[Code] COLLATE Latin1_General_BIN2),
       CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [dbo].[Roles] o WITH (UPDLOCK, HOLDLOCK)
                WHERE o.[Id] <> r.[Id]
                  AND ((o.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL)
                       OR o.[ApplicationId] = r.[ApplicationId])
                  AND LOWER(o.[Code] COLLATE Latin1_General_BIN2)
                      = LOWER(r.[Code] COLLATE Latin1_General_BIN2))
            THEN 1 ELSE 0 END AS BIT)
FROM [dbo].[Roles] r WITH (UPDLOCK, HOLDLOCK)
WHERE r.[Code] COLLATE Latin1_General_BIN2 <> LOWER(r.[Code] COLLATE Latin1_General_BIN2);

-- Every role about to change, BEFORE it changes. Keep this output: it is the
-- notice to the relying parties and the undo list.
SELECT c.[Id], c.[ApplicationId], a.[Code] AS [ApplicationCode], c.[OldCode], c.[NewCode]
FROM @Candidates c
LEFT JOIN [dbo].[Applications] a ON a.[Id] = c.[ApplicationId]
WHERE c.[Collides] = 0
ORDER BY a.[Code], c.[OldCode];

-- Not changed: another role of the same scope has, or would get, the same
-- lowercase code.
SELECT c.[Id] AS [SkippedId], c.[ApplicationId], a.[Code] AS [ApplicationCode], c.[OldCode], c.[NewCode]
FROM @Candidates c
LEFT JOIN [dbo].[Applications] a ON a.[Id] = c.[ApplicationId]
WHERE c.[Collides] = 1
ORDER BY a.[Code], c.[OldCode];

UPDATE r SET
    r.[Code] = c.[NewCode],
    r.[ModifiedAt] = GETUTCDATE(),
    r.[ModifiedBy] = @SystemUserId
FROM [dbo].[Roles] r
INNER JOIN @Candidates c ON c.[Id] = r.[Id]
WHERE c.[Collides] = 0;

DECLARE @Lowercased INT = @@ROWCOUNT;
DECLARE @Skipped INT = (SELECT COUNT(*) FROM @Candidates WHERE [Collides] = 1);

COMMIT TRANSACTION;

PRINT CONCAT(N'Role codes lowercased: ', @Lowercased,
    N'. Skipped because the same scope has the lowercase code: ', @Skipped,
    N'. Send the first list to the relying parties and keep it as the undo list.');
GO
