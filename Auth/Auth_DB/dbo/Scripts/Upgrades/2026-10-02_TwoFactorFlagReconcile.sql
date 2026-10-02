-- ============================================================================
-- 2026-10-02 — Reconcile the two-factor account flag with the factor row (X02)
--
-- MANUAL. Run by the owner once, after the API that writes the flag only with
-- the factor row is deployed, and only if the read-only inventory found accounts
-- whose two values disagree. Deliberately NOT in the post-deployment chain: it
-- changes which accounts are asked for a second factor, so it never runs on a
-- publish by itself.
--
-- Why accounts can disagree: until that API, the whole-row user write
-- (UserRepository.UpdateAsync) wrote Users.IsTwoFactorEnabled from a copy of the
-- user read earlier — an avatar adopted at sign-in, a profile edit — so a stale
-- copy could switch the flag off, and a failure between the two writes of enable
-- or disable could leave them apart. The sign-in gate reads the flag; second-
-- factor verification reads TwoFactorAuth.IsEnabled. When they disagree:
--
--   flag 1, no enabled row  -> every sign-in stops at a second-factor step that
--                              can never succeed. After this script the password
--                              alone signs in: tell the account to enrol again.
--   flag 0, enabled row     -> sign-in skips a factor the user enrolled. After
--                              this script sign-in asks for it again: tell the
--                              account why.
--
-- What it does: sets the flag to whether an enabled factor row exists — the row
-- is what verification reads. Every account it is about to change is SELECTed
-- first (Id, Email, the flag before and after), so each can be notified in the
-- direction of its change and each change undone on its own.
--
-- Idempotent: a second run finds nothing to change and prints an empty list.
-- Undo, one printed row: UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = <FlagBefore> WHERE [Id] = '<Id>';
-- Run at a quiet time: an enable or disable in flight can make this transaction
-- a deadlock victim, which rolls it back whole — run it again.
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Changes TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Email] NVARCHAR(255) NOT NULL,
    [FlagBefore] BIT NOT NULL,
    [FlagAfter] BIT NOT NULL
);

BEGIN TRANSACTION;

-- The accounts whose flag disagrees with their factor row. Both rows are held
-- until the end — the user's and its factor rows — so the list printed below is
-- exactly the list changed, and an enable or disable cannot commit between the
-- read and the update (whichever table the plan reads first): it waits, or one
-- of the two is chosen as a deadlock victim and rolls back whole.
INSERT INTO @Changes ([Id], [Email], [FlagBefore], [FlagAfter])
SELECT u.[Id], u.[Email], u.[IsTwoFactorEnabled], f.[HasEnabledFactor]
FROM [dbo].[Users] u WITH (UPDLOCK, HOLDLOCK)
CROSS APPLY (
    SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1
                    FROM [dbo].[TwoFactorAuth] t WITH (UPDLOCK, HOLDLOCK)
                    WHERE t.[UserId] = u.[Id]
                      AND t.[IsEnabled] = 1)
                THEN 1 ELSE 0 END AS BIT) AS [HasEnabledFactor]
) f
WHERE u.[IsTwoFactorEnabled] <> f.[HasEnabledFactor];

-- Every account about to change, BEFORE it changes. Keep this output: it is the
-- notification list and the undo list.
SELECT [Id], [Email], [FlagBefore], [FlagAfter]
FROM @Changes
ORDER BY [FlagAfter], [Email];

UPDATE u SET
    u.[IsTwoFactorEnabled] = c.[FlagAfter],
    u.[ModifiedAt] = SYSUTCDATETIME()
FROM [dbo].[Users] u
INNER JOIN @Changes c ON c.[Id] = u.[Id];

DECLARE @Reconciled INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CONCAT(N'Two-factor flags reconciled: ', @Reconciled,
    N'. Notify every printed address in the direction of its change (FlagAfter 0: enrol again; FlagAfter 1: the second factor is asked for again).');
GO
