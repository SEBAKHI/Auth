-- Drop three stored procedures that nothing calls, from EXISTING databases (X12 PR B).
--
-- sp_ValidateCredentials, sp_RecordLoginAttempt and sp_CheckAccountLockout were
-- never invoked by any code. Two of them wrote the account-lock columns of Users
-- (Status, FailedLoginAttempts, LockoutEndUtc) by rules the API no longer follows:
-- sp_CheckAccountLockout zeroed the failure count when a lock lapsed, which is the
-- very behaviour X12 removed. Leaving them in a database leaves a second, wrong
-- writer of those columns one EXEC away.
--
-- Their files left the project in the same change, so a fresh database never has
-- them. An existing one keeps them after a publish: DropObjectsNotInSource is False
-- by default and in the deployment guide's publish settings, so the DACPAC does not
-- drop objects it no longer defines. This script does, on every publish.
--
-- The repository's rule for changes to existing databases (Auth_DB/README.md,
-- "Changing seeded data"): a dated, idempotent script under dbo/Scripts/Upgrades,
-- included from the post-deployment script, and deleted once every known
-- deployment has run it. Idempotent: DROP PROCEDURE IF EXISTS does nothing where
-- the procedure is already gone, so a fresh database and every publish after the
-- first run it as a no-op.
--
-- sqlcmd-safe: pure ASCII, no BOM, QUOTED_IDENTIFIER ON, and its own GO at the end.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DROP PROCEDURE IF EXISTS [dbo].[sp_CheckAccountLockout];
DROP PROCEDURE IF EXISTS [dbo].[sp_RecordLoginAttempt];
DROP PROCEDURE IF EXISTS [dbo].[sp_ValidateCredentials];

PRINT 'Dead lock procedures absent (sp_CheckAccountLockout, sp_RecordLoginAttempt, sp_ValidateCredentials)';
GO
