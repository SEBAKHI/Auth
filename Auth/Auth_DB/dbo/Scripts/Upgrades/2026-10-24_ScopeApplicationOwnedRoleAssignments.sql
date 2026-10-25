-- Give every assignment of an application-owned role its application's scope, in
-- EXISTING databases (OI-223).
--
-- Assigning a role from a user's page wrote UserRoles.ApplicationId = NULL even when
-- an application owns the role (Roles.ApplicationId set). Such a row already works
-- only in that application's tokens: the application reads join on the role's owner,
-- and every platform read requires both scope columns to be NULL. So it is the same
-- authority as a row at the role's application, written in a shape that showed as a
-- platform role and blocked nothing. The API now writes the scoped shape; this script
-- moves the old rows to it. Who holds what does not change.
--
-- For each such row:
-- - no row of the same (user, role) at the role's application: the row moves there;
-- - one exists: the scoped row is kept with the wider of the two lifetimes (an active
--   row over an inactive one; then no expiry over any date; then the later date),
--   and the unscoped row is deleted. UQ_UserRoles allows one row per scope, so a
--   plain move would violate the key.
-- Not touched: rows of an application-owned role scoped to ANOTHER application, and
-- every row of a global role. No access grant is written: that would change who may
-- sign in to a Restricted application.
--
-- The repository's rule for changes to existing databases (Auth_DB/README.md,
-- "Changing seeded data"): a dated, idempotent script under dbo/Scripts/Upgrades,
-- included from the post-deployment script, and deleted once every known deployment
-- has run it. Idempotent: once moved, no row matches, so a fresh database and every
-- publish after the first change 0 rows.
--
-- sqlcmd-safe: pure ASCII, no BOM, QUOTED_IDENTIFIER ON, and its own GO at the end.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Widened INT, @Merged INT, @Moved INT;

-- The API may still be running while the DACPAC publishes. SERIALIZABLE keeps the
-- rows each step read locked until the commit, so a scoped row inserted between the
-- merge and the move cannot turn the move into a duplicate-key failure.
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

-- 1. Where both rows exist and the unscoped one lives longer, the scoped row takes its lifetime.
UPDATE scoped
SET [AssignedAt] = unscoped.[AssignedAt],
    [AssignedBy] = unscoped.[AssignedBy],
    [ExpiresAt] = unscoped.[ExpiresAt],
    [IsActive] = unscoped.[IsActive]
FROM [dbo].[UserRoles] unscoped
INNER JOIN [dbo].[Roles] r ON r.[Id] = unscoped.[RoleId]
INNER JOIN [dbo].[UserRoles] scoped WITH (UPDLOCK, HOLDLOCK)
    ON scoped.[UserId] = unscoped.[UserId]
   AND scoped.[RoleId] = unscoped.[RoleId]
   AND scoped.[ApplicationId] = r.[ApplicationId]
WHERE unscoped.[ApplicationId] IS NULL
  AND r.[ApplicationId] IS NOT NULL
  AND (
        -- An active row over an inactive one (bits: 1 > 0). Written as a comparison:
        -- PermissionSeedCoverageTests refuses any "IsActive = 0" in the publish script.
        scoped.[IsActive] < unscoped.[IsActive]
     OR (unscoped.[IsActive] = scoped.[IsActive]
         AND scoped.[ExpiresAt] IS NOT NULL
         AND (unscoped.[ExpiresAt] IS NULL OR unscoped.[ExpiresAt] > scoped.[ExpiresAt]))
  );
SET @Widened = @@ROWCOUNT;

-- 2. The unscoped duplicate of a scoped row goes.
DELETE unscoped
FROM [dbo].[UserRoles] unscoped
INNER JOIN [dbo].[Roles] r ON r.[Id] = unscoped.[RoleId]
WHERE unscoped.[ApplicationId] IS NULL
  AND r.[ApplicationId] IS NOT NULL
  AND EXISTS (
        SELECT 1
        FROM [dbo].[UserRoles] scoped
        WHERE scoped.[UserId] = unscoped.[UserId]
          AND scoped.[RoleId] = unscoped.[RoleId]
          AND scoped.[ApplicationId] = r.[ApplicationId]
  );
SET @Merged = @@ROWCOUNT;

-- 3. Every remaining unscoped row of an application-owned role moves to its application.
UPDATE unscoped
SET [ApplicationId] = r.[ApplicationId]
FROM [dbo].[UserRoles] unscoped
INNER JOIN [dbo].[Roles] r ON r.[Id] = unscoped.[RoleId]
WHERE unscoped.[ApplicationId] IS NULL
  AND r.[ApplicationId] IS NOT NULL;
SET @Moved = @@ROWCOUNT;

COMMIT TRANSACTION;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

PRINT 'Application-owned role assignments scoped to their application: moved '
    + CAST(@Moved AS VARCHAR(10)) + ', merged into an existing scoped row '
    + CAST(@Merged AS VARCHAR(10)) + ' (of which the scoped row took the wider lifetime '
    + CAST(@Widened AS VARCHAR(10)) + ')';
GO
