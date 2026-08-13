/*
=============================================================
  ONE-TIME RECONCILIATION: Default constraint names
  Target: company_identity_prod (shared hosting)
=============================================================

WHY THIS EXISTS
---------------
The Auth_DB project originally declared every DEFAULT constraint inline and
unnamed, e.g.  [Status] TINYINT NOT NULL DEFAULT 1.
SQL Server then assigned each one a RANDOM, per-database name such as
'DF__Users__Status__39AD8A7F'. Because those names differ between the project
model and the live prod database, an SSDT/DacFx publish could not match them and
reported "unnamed constraint on [dbo].[Users] will be dropped and not re-created",
which BlockOnPossibleDataLoss=True turned into a hard failure at preview.

The project now names every default constraint deterministically as
DF_<Table>_<Column>. This script aligns the EXISTING prod database to those names
so the next publish sees no difference (no drop/add, no table rebuild).

WHAT IT DOES
------------
Renames every auto-generated default constraint (name begins with 'DF__') to the
canonical DF_<Table>_<Column> name. sp_rename is METADATA-ONLY:
  - no table rebuild
  - no data movement
  - no data loss
It is idempotent: already-correct constraints are skipped, so it is safe to run
more than once.

HOW TO USE
----------
1. Connect to the company_identity_prod database (SSMS / Plesk SQL Manager) using
   the company_identity_rw_prod credentials. Make sure you are in the
   correct database context.
2. Run the script. It first PRINTs the rename statements for review, then runs
   them inside a transaction.
3. After it completes, run an SSDT "Generate Script" against prod and confirm the
   Users diff is empty before publishing.

NOTE: This handles DEFAULT constraints only. The named CHECK constraints
(CK_Users_Status, CK_OrganizationInvitations_Status) are created by name from the
project; if prod is missing one, the publish will ADD it and validate existing
rows. Confirm the data conforms before publishing if you see that in the diff.
=============================================================
*/

SET NOCOUNT ON;

DECLARE @renames TABLE
(
    OldName   SYSNAME,
    NewName   SYSNAME,
    TableName SYSNAME,
    ColumnName SYSNAME
);

INSERT INTO @renames (OldName, NewName, TableName, ColumnName)
SELECT  dc.name                                        AS OldName,
        N'DF_' + t.name + N'_' + c.name                AS NewName,
        t.name                                         AS TableName,
        c.name                                         AS ColumnName
FROM    sys.default_constraints dc
JOIN    sys.tables  t ON dc.parent_object_id = t.object_id
JOIN    sys.columns c ON c.object_id = dc.parent_object_id
                     AND c.column_id = dc.parent_column_id
WHERE   t.schema_id = SCHEMA_ID(N'dbo')
  AND   dc.name LIKE N'DF[_][_]%'                       -- only SQL Server auto-named (DF__...)
  AND   dc.name <> N'DF_' + t.name + N'_' + c.name;     -- skip already-correct

IF NOT EXISTS (SELECT 1 FROM @renames)
BEGIN
    PRINT N'No auto-named default constraints found. Nothing to reconcile.';
    RETURN;
END

-- 1) Review: print what will be renamed
DECLARE @old SYSNAME, @new SYSNAME, @tbl SYSNAME, @col SYSNAME;

PRINT N'The following default constraints will be renamed:';
PRINT N'---------------------------------------------------';
DECLARE review_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT OldName, NewName, TableName, ColumnName FROM @renames ORDER BY TableName, ColumnName;
OPEN review_cur;
FETCH NEXT FROM review_cur INTO @old, @new, @tbl, @col;
WHILE @@FETCH_STATUS = 0
BEGIN
    PRINT N'  [' + @tbl + N'].[' + @col + N']: ' + @old + N'  ->  ' + @new;
    FETCH NEXT FROM review_cur INTO @old, @new, @tbl, @col;
END
CLOSE review_cur;
DEALLOCATE review_cur;

-- 2) Apply renames in a transaction
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE apply_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT OldName, NewName FROM @renames;
    OPEN apply_cur;
    FETCH NEXT FROM apply_cur INTO @old, @new;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC sys.sp_rename @objname = @old, @newname = @new, @objtype = 'OBJECT';
        FETCH NEXT FROM apply_cur INTO @old, @new;
    END
    CLOSE apply_cur;
    DEALLOCATE apply_cur;

    COMMIT TRANSACTION;
    PRINT N'';
    PRINT N'Reconciliation complete. All auto-named default constraints renamed to DF_<Table>_<Column>.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT N'Reconciliation FAILED and was rolled back.';
    THROW;
END CATCH;
GO
