-- Mark the recovery codes of every enabled authenticator app as qualified, on
-- EXISTING databases (S24, ARR-0692).
--
-- TwoFactorAuth.RecoveryCodesQualified says whether one of the account's recovery
-- codes counts as a second factor. Codes issued when an authenticator app is
-- switched on, or renewed in a session that proved two factors, do (1); codes
-- issued when the emailed code is switched on, in a session that may have proved
-- the password alone, do not (0). The column's default is 0, so that a writer that
-- forgets to name it fails closed rather than open; the publish therefore gives
-- every existing row 0, including the enabled authenticator apps whose codes have
-- always counted. This statement gives them back the 1 they had in meaning.
--
-- Idempotent, and correct on every publish, not only the first: no path stores
-- unqualified codes on a row with IsEnabled = 1 (switching the authenticator app
-- on writes 1, and the emailed code is never bound beside it,
-- CK_TwoFactorAuth_SingleFactor), so on a fresh database, and on every publish
-- after the first, it changes nothing.
--
-- sqlcmd-safe: pure ASCII, no BOM, QUOTED_IDENTIFIER ON, and its own GO at the end.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

UPDATE [dbo].[TwoFactorAuth]
SET [RecoveryCodesQualified] = 1
WHERE [IsEnabled] = 1
  AND [RecoveryCodesQualified] = 0;

PRINT 'Recovery codes of enabled authenticator apps qualified';
GO
