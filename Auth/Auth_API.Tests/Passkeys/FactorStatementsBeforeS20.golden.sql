-- REVIEW 150 F6: the three FA2 statements as main sent them before S20 (origin/main,
-- TwoFactorStateStore: HasEnabledFactorSql, ApplicationMfaReadinessSql,
-- HasPlatformRoleHolderWithoutFactorSql, each with EnabledFactorOf filled in). Without an
-- RP ID or the passkey schema the store must send exactly these; the test compares them
-- with whitespace collapsed. Generated, not typed: never edit a statement by hand.

-- @@ HasEnabledFactor
SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[TwoFactorAuth]
                WHERE [UserId] = @UserId AND [IsEnabled] = 1)
                THEN 1 ELSE 0 END AS BIT)

-- @@ ApplicationMfaReadiness
SELECT
                COUNT(*) AS [ActiveUsers],
                ISNULL(SUM(u.[HasFactor]), 0) AS [WithQualifyingFactor]
            FROM (
                SELECT CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[TwoFactorAuth]
                WHERE [UserId] = a.[UserId] AND [IsEnabled] = 1)
                    THEN 1 ELSE 0 END AS [HasFactor]
                FROM (
                    SELECT DISTINCT s.[UserId]
                    FROM [dbo].[UserSessions] s
                    INNER JOIN [dbo].[Users] usr ON usr.[Id] = s.[UserId] AND usr.[IsDeleted] = 0
                    WHERE s.[ApplicationId] = @ApplicationId
                      AND s.[StartedAt] >= @Since) a
            ) u

-- @@ HasPlatformRoleHolderWithoutFactor
SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserRoles] ur
                INNER JOIN [dbo].[Roles] r ON r.[Id] = ur.[RoleId]
                WHERE ur.[RoleId] = @RoleId
                  AND ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL
                  AND ur.[IsActive] = 1 AND r.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
                  AND NOT EXISTS (
                SELECT 1 FROM [dbo].[TwoFactorAuth]
                WHERE [UserId] = ur.[UserId] AND [IsEnabled] = 1))
                THEN 1 ELSE 0 END AS BIT)
