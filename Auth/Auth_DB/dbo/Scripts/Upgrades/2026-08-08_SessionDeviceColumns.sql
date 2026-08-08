-- ============================================================================
-- 2026-08-08 — Unpack the device id out of UserSessions.UserAgent
--
-- Until now the client's per-browser identifier was concatenated into the user
-- agent before the session row was written:
--
--     "{userAgent} | DeviceId: {uuid}"
--
-- That had two costs. The combined string could exceed UserAgent's 500
-- characters, and the resulting insert failure was swallowed by the caller's
-- catch — producing a successful sign-in with no session row at all. And the
-- identifier sat in a column nothing could join on, so a session could never be
-- attributed to the browser that started it.
--
-- The application now writes DeviceId, DeviceName, DeviceType and DeviceHash as
-- their own columns. This script recovers what it can from the rows written
-- before that change. It is idempotent and can be run more than once.
--
-- DELIBERATELY NOT BACKFILLED: DeviceHash. The signature is SHA-256 over the
-- device id plus the browser and OS *families*, and those families come from an
-- ordered set of regular expressions in UserAgentParser — Edge before Chrome,
-- Chrome before Safari. Reimplementing that ordering in T-SQL would produce a
-- second classifier that agrees with the first until the day it does not, and a
-- wrong hash does not fail loudly: it silently files a session under the wrong
-- browser, or invents one. Historical rows keep a NULL hash and surface as
-- unattributed sessions, which is the truth about them. They age out on their
-- own within the refresh-token lifetime.
-- ============================================================================

SET XACT_ABORT ON;
GO

-- ---------------------------------------------------------------------------
-- 1) Recover the device id, and restore UserAgent to just the user agent.
--
-- Guarded on DeviceId IS NULL so a re-run cannot re-split an already-split row,
-- and on the separator being present so rows that never carried an id are left
-- alone. LEFT(...) is bounded by the separator's position, so the rewrite can
-- only ever shorten the value.
-- ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.UserSessions', 'DeviceId') IS NOT NULL
BEGIN
    UPDATE [dbo].[UserSessions]
    SET [DeviceId] = NULLIF(LTRIM(RTRIM(
            SUBSTRING([UserAgent],
                      CHARINDEX(' | DeviceId: ', [UserAgent]) + LEN(' | DeviceId: '),
                      64))), ''),
        [UserAgent] = NULLIF(LEFT([UserAgent],
                      CHARINDEX(' | DeviceId: ', [UserAgent]) - 1), '')
    WHERE [DeviceId] IS NULL
      AND [UserAgent] IS NOT NULL
      AND CHARINDEX(' | DeviceId: ', [UserAgent]) > 0;

    -- The id-only form, written when the client sent no user agent at all.
    UPDATE [dbo].[UserSessions]
    SET [DeviceId] = NULLIF(LTRIM(RTRIM(
            SUBSTRING([UserAgent], LEN('DeviceId: ') + 1, 64))), ''),
        [UserAgent] = NULL
    WHERE [DeviceId] IS NULL
      AND [UserAgent] IS NOT NULL
      AND [UserAgent] LIKE 'DeviceId: %';
END
GO

-- ---------------------------------------------------------------------------
-- 2) DeviceType previously received the entity's DeviceName (always NULL at
--    every call site), so the column is empty rather than wrong. Any value that
--    did land there is not one of the four documented form factors, and the
--    application reads it as an enum — clear it so a stray label cannot fail a
--    parse. The column is repopulated correctly from the next sign-in onward.
-- ---------------------------------------------------------------------------
UPDATE [dbo].[UserSessions]
SET [DeviceType] = NULL
WHERE [DeviceType] IS NOT NULL
  AND [DeviceType] NOT IN ('desktop', 'mobile', 'tablet', 'unknown');
GO
