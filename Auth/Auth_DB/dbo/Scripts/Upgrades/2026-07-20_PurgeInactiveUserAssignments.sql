-- Purge deactivated direct assignment rows for EXISTING databases (dev/prod).
--
-- Background: role/permission removal used to soft-delete ([IsActive] = 0), but
-- UQ_UserRoles (UserId, RoleId, ApplicationId) and UQ_UserPermissions
-- (UserId, PermissionId, ApplicationId) have no [IsActive] filter. Re-assigning a
-- previously removed role therefore collided with the dead row and failed with
-- "Violation of UNIQUE KEY constraint 'UQ_UserRoles'" (SQL error 2627).
--
-- Removal is now a hard DELETE (see RoleRepository / UserRepository /
-- PermissionRepository); this script clears the rows left behind by the old
-- behaviour so already-poisoned (user, role) pairs can be re-assigned.
--
-- Every read path filters [IsActive] = 1, so these rows are invisible to the
-- application and carry no information the audit log does not already hold.
-- Idempotent: re-running it simply deletes nothing.

-- UserRoles/UserPermissions carry filtered indexes, and any DML against them requires
-- QUOTED_IDENTIFIER ON. SqlClient sets it by default; sqlcmd does NOT, so set it here
-- or the DELETE fails with "SET options have incorrect settings" (Msg 1934).
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @DeletedRoles INT = 0;
DECLARE @DeletedPermissions INT = 0;

DELETE FROM [dbo].[UserRoles] WHERE [IsActive] = 0;
SET @DeletedRoles = @@ROWCOUNT;

DELETE FROM [dbo].[UserPermissions] WHERE [IsActive] = 0;
SET @DeletedPermissions = @@ROWCOUNT;

PRINT 'Purged inactive UserRoles rows: ' + CAST(@DeletedRoles AS NVARCHAR(20));
PRINT 'Purged inactive UserPermissions rows: ' + CAST(@DeletedPermissions AS NVARCHAR(20));
GO
