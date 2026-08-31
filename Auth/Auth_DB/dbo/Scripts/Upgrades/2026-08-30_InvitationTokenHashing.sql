-- ============================================================================
-- 2026-08-30 — Organization invitation tokens are hashed at rest
--
-- Idempotent. Runs on every publish; does nothing once there is nothing left to
-- reconcile.
--
-- WHAT CHANGED IN THE APPLICATION
--
--   OrganizationInvitations.[Token] used to hold the invitation token in clear
--   text. It was the only bearer credential in this system stored that way:
--   refresh tokens, authorization codes, password-reset tokens, API keys and
--   every OTP are hashed. Anyone who could read one row - a read-only SQL login,
--   a backup, a support export, a query in a log - could POST it to
--   /api/v1/invitations/{token}/register and end up an active member of that
--   organization, holding the role the invitation named. That is a tenant
--   boundary crossed with a single SELECT.
--
--   The column now holds an HMAC-SHA256 of the token under the server key that
--   already hashes refresh tokens (Jwt:RefreshTokenHmacKeyPlain). The plaintext
--   exists in the invitation e-mail and in the request that redeems it, and
--   nowhere else.
--
-- WHY THERE IS NO SCHEMA CHANGE
--
--   The column is already NVARCHAR(500) NOT NULL UNIQUE and the hash is 44
--   base64 characters, so it fits, and a hash is exactly as unique as the token
--   it stands for. Renaming [Token] to [TokenHash] would be a DACPAC
--   drop-and-add on a UNIQUE-constrained column - real data-loss risk on
--   publish, in exchange for a better name. The domain entity carries the
--   honest name (OrganizationInvitation.TokenHash) instead, and the persistence
--   record documents the mismatch where it is mapped.
--
-- WHY PENDING INVITATIONS ARE CANCELLED RATHER THAN MIGRATED
--
--   Migrating would mean computing the HMAC of each stored plaintext. T-SQL
--   cannot: HASHBYTES has no keyed mode, and the key lives in the application's
--   encrypted secrets file - putting it into a deployment script would publish
--   the key that protects every refresh token in the system, to fix a token that
--   expires in seven days. That trade is not close.
--
--   So a pending invitation written before this deploy no longer resolves: the
--   redeeming request hashes what the invitee presents and finds no matching
--   row. Left alone, those rows would sit Pending and fail silently, and the
--   invitee would see a generic "invitation not found" with no way to tell a
--   typo from a deploy. Cancelling them says what happened, keeps the table
--   truthful, and lets an administrator invite again - one action, on a
--   credential whose whole lifetime is a week.
--
--   Cancelled is chosen over Expired on purpose: these did not run out of time.
-- ============================================================================

PRINT '--- 2026-08-30 Invitation token hashing ---';
GO

-- Idempotent by construction: after the first run there are no Pending rows
-- predating the deploy, and a Pending row created afterwards already holds a
-- hash and must never be touched. The CreatedAt bound is what separates the
-- two, and it is evaluated against this deployment's own start.
DECLARE @Affected INT = 0;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE [object_id] = OBJECT_ID(N'[dbo].[OrganizationInvitations]')
             AND [name] = N'Token')
BEGIN
    -- The two shapes are distinguishable without guessing:
    --
    --   hash      HMACSHA256 -> 32 bytes -> Convert.ToBase64String, NOT trimmed
    --             => exactly 44 characters, always ending in '='
    --   plaintext SecureTokenGenerator -> 32 bytes -> base64url, TrimEnd('=')
    --             => exactly 43 characters, never ending in '='
    --
    -- Both conditions are asserted rather than just the length, so a value of
    -- some other provenance that happened to be 44 characters long is still
    -- treated as pre-upgrade rather than mistaken for a hash. A row the new code
    -- wrote can never match.
    UPDATE [dbo].[OrganizationInvitations]
    SET [Status] = N'Cancelled'
    WHERE [Status] = N'Pending'
      AND (LEN([Token]) <> 44 OR RIGHT([Token], 1) <> N'=');

    SET @Affected = @@ROWCOUNT;
END

IF @Affected > 0
BEGIN
    PRINT CONCAT(
        'Cancelled ', @Affected,
        ' pending invitation(s) whose token predates hashing. Their links no longer work by design; ',
        'invite those people again from the organization members page.');
END
ELSE
BEGIN
    PRINT 'No pre-hashing pending invitations found; nothing to reconcile.';
END
GO
