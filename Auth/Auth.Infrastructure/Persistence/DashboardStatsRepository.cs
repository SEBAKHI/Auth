using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Dashboard;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the dashboard statistics repository.
/// Every metric is computed in SQL over the full tables (never over a page),
/// with UTC day bucketing and parameterized trailing windows.
/// </summary>
public class DashboardStatsRepository : IDashboardStatsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DashboardStatsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserStatsSnapshot> GetUserStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sqlTimeZone = ToSqlServerTimeZone(timeZone);

        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();
            DECLARE @From DATETIME2 = DATEADD(DAY, -@Days, @Now);

            -- Totals, MFA adoption, window cohort funnel, dormancy (non-deleted users only)
            SELECT
                COUNT(*) AS TotalUsers,
                ISNULL(SUM(CASE WHEN [Status] = 1 THEN 1 ELSE 0 END), 0) AS ActiveUsers,
                ISNULL(SUM(CASE WHEN [Status] = 1 AND [IsTwoFactorEnabled] = 1 THEN 1 ELSE 0 END), 0) AS MfaEnabled,
                ISNULL(SUM(CASE WHEN [CreatedAt] >= @From THEN 1 ELSE 0 END), 0) AS NewInWindow,
                ISNULL(SUM(CASE WHEN [CreatedAt] >= @From AND [IsEmailConfirmed] = 1 THEN 1 ELSE 0 END), 0) AS CohortEmailConfirmed,
                ISNULL(SUM(CASE WHEN [CreatedAt] >= @From AND [LastLoginUtc] IS NOT NULL THEN 1 ELSE 0 END), 0) AS CohortLoggedIn,
                ISNULL(SUM(CASE WHEN [Status] = 1 AND COALESCE([LastLoginUtc], [CreatedAt]) < DATEADD(DAY, -30, @Now) THEN 1 ELSE 0 END), 0) AS DormantOver30Days,
                ISNULL(SUM(CASE WHEN [Status] = 1 AND COALESCE([LastLoginUtc], [CreatedAt]) < DATEADD(DAY, -60, @Now) THEN 1 ELSE 0 END), 0) AS DormantOver60Days,
                ISNULL(SUM(CASE WHEN [Status] = 1 AND COALESCE([LastLoginUtc], [CreatedAt]) < DATEADD(DAY, -90, @Now) THEN 1 ELSE 0 END), 0) AS DormantOver90Days,
                ISNULL(SUM(CASE WHEN [Status] = 1 AND [LastLoginUtc] IS NULL THEN 1 ELSE 0 END), 0) AS NeverLoggedIn
            FROM [dbo].[Users]
            WHERE [IsDeleted] = 0;

            -- Status mix
            SELECT [Status], COUNT(*) AS [Count]
            FROM [dbo].[Users]
            WHERE [IsDeleted] = 0
            GROUP BY [Status];

            -- Signups per viewer-local calendar day inside the UTC instant window
            SELECT CAST(([CreatedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE) AS [Date],
                   COUNT(*) AS [Count]
            FROM [dbo].[Users]
            WHERE [IsDeleted] = 0 AND [CreatedAt] >= @From
            GROUP BY CAST(([CreatedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE)
            ORDER BY [Date];

            -- Ten largest organizations by active membership
            SELECT TOP (10)
                o.[Id] AS OrganizationId,
                o.[Name] AS OrganizationName,
                o.[IsAutoCreated],
                COUNT(ou.[Id]) AS [Count]
            FROM [dbo].[Organizations] o
            JOIN [dbo].[OrganizationUsers] ou
                ON ou.[OrganizationId] = o.[Id] AND ou.[IsActive] = 1
            WHERE o.[IsActive] = 1
            GROUP BY o.[Id], o.[Name], o.[IsAutoCreated]
            ORDER BY [Count] DESC;

            -- Total active memberships (denominator for the 'Other' bucket)
            SELECT COUNT(*)
            FROM [dbo].[OrganizationUsers] ou
            JOIN [dbo].[Organizations] o ON o.[Id] = ou.[OrganizationId] AND o.[IsActive] = 1
            WHERE ou.[IsActive] = 1;",
            new { Days = days, TimeZone = sqlTimeZone });

        var totals = await grid.ReadSingleAsync<UserTotalsRow>();
        var byStatus = (await grid.ReadAsync<UserStatusCount>()).ToList();
        var signupsPerDay = (await grid.ReadAsync<DailyCount>()).ToList();
        var usersByOrganization = (await grid.ReadAsync<OrganizationUserCount>()).ToList();
        var totalActiveMemberships = await grid.ReadSingleAsync<int>();

        return new UserStatsSnapshot
        {
            TotalUsers = totals.TotalUsers,
            ByStatus = byStatus,
            MfaEnabled = totals.MfaEnabled,
            ActiveUsers = totals.ActiveUsers,
            NewInWindow = totals.NewInWindow,
            SignupsPerDay = signupsPerDay,
            CohortCreated = totals.NewInWindow,
            CohortEmailConfirmed = totals.CohortEmailConfirmed,
            CohortLoggedIn = totals.CohortLoggedIn,
            DormantOver30Days = totals.DormantOver30Days,
            DormantOver60Days = totals.DormantOver60Days,
            DormantOver90Days = totals.DormantOver90Days,
            NeverLoggedIn = totals.NeverLoggedIn,
            UsersByOrganization = usersByOrganization,
            TotalActiveMemberships = totalActiveMemberships
        };
    }

    /// <inheritdoc />
    public async Task<AuthStatsSnapshot> GetAuthStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sqlTimeZone = ToSqlServerTimeZone(timeZone);

        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();
            DECLARE @From DATETIME2 = DATEADD(DAY, -@Days, @Now);
            DECLARE @PrevFrom DATETIME2 = DATEADD(DAY, -@Days, @From);

            -- Attempts per viewer-local calendar day, split by outcome
            SELECT CAST(([AttemptedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE) AS [Date],
                   SUM(CASE WHEN [IsSuccessful] = 1 THEN 1 ELSE 0 END) AS SuccessCount,
                   SUM(CASE WHEN [IsSuccessful] = 0 THEN 1 ELSE 0 END) AS FailureCount
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From
            GROUP BY CAST(([AttemptedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE)
            ORDER BY [Date];

            -- Daily active users (distinct users with a successful login)
            SELECT CAST(([AttemptedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE) AS [Date],
                   COUNT(DISTINCT [UserId]) AS [Count]
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [IsSuccessful] = 1 AND [UserId] IS NOT NULL
            GROUP BY CAST(([AttemptedAt] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE)
            ORDER BY [Date];

            -- Distinct active users across the whole window
            SELECT COUNT(DISTINCT [UserId])
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [IsSuccessful] = 1 AND [UserId] IS NOT NULL;

            -- Failure reasons, most frequent first
            SELECT ISNULL([FailureReason], N'unknown') AS Reason, COUNT(*) AS [Count]
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [IsSuccessful] = 0
            GROUP BY ISNULL([FailureReason], N'unknown')
            ORDER BY [Count] DESC;

            -- Current and previous window outcome totals (for deltas)
            SELECT
                ISNULL(SUM(CASE WHEN [AttemptedAt] >= @From AND [IsSuccessful] = 1 THEN 1 ELSE 0 END), 0) AS WindowSuccessCount,
                ISNULL(SUM(CASE WHEN [AttemptedAt] >= @From AND [IsSuccessful] = 0 THEN 1 ELSE 0 END), 0) AS WindowFailureCount,
                ISNULL(SUM(CASE WHEN [AttemptedAt] < @From AND [IsSuccessful] = 1 THEN 1 ELSE 0 END), 0) AS PreviousWindowSuccessCount,
                ISNULL(SUM(CASE WHEN [AttemptedAt] < @From AND [IsSuccessful] = 0 THEN 1 ELSE 0 END), 0) AS PreviousWindowFailureCount
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @PrevFrom;

            -- Users currently locked out
            SELECT COUNT(*)
            FROM [dbo].[Users]
            WHERE [IsDeleted] = 0 AND [LockoutEndUtc] > @Now;

            -- Attempts rejected because the account was locked
            -- ('Account locked' is the literal written by the login flow)
            SELECT COUNT(*)
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [FailureReason] = N'Account locked';

            -- Top failing IP addresses
            SELECT TOP (10)
                [IpAddress],
                COUNT(*) AS FailureCount,
                COUNT(DISTINCT [Username]) AS DistinctUsernames
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [IsSuccessful] = 0
            GROUP BY [IpAddress]
            ORDER BY FailureCount DESC;

            -- Outcomes per application (NULL application kept as its own bucket)
            SELECT la.[ApplicationId],
                   a.[Name] AS ApplicationName,
                   SUM(CASE WHEN la.[IsSuccessful] = 1 THEN 1 ELSE 0 END) AS SuccessCount,
                   SUM(CASE WHEN la.[IsSuccessful] = 0 THEN 1 ELSE 0 END) AS FailureCount
            FROM [dbo].[LoginAttempts] la
            LEFT JOIN [dbo].[Applications] a ON a.[Id] = la.[ApplicationId]
            WHERE la.[AttemptedAt] >= @From
            GROUP BY la.[ApplicationId], a.[Name]
            ORDER BY COUNT(*) DESC;

            -- Outcomes attributed to the ten most active organizations via membership.
            -- A user in several organizations is counted once per organization.
            SELECT TOP (10)
                ou.[OrganizationId],
                o.[Name] AS OrganizationName,
                SUM(CASE WHEN la.[IsSuccessful] = 1 THEN 1 ELSE 0 END) AS SuccessCount,
                SUM(CASE WHEN la.[IsSuccessful] = 0 THEN 1 ELSE 0 END) AS FailureCount
            FROM [dbo].[LoginAttempts] la
            JOIN [dbo].[OrganizationUsers] ou ON ou.[UserId] = la.[UserId] AND ou.[IsActive] = 1
            JOIN [dbo].[Organizations] o ON o.[Id] = ou.[OrganizationId]
            WHERE la.[AttemptedAt] >= @From
            GROUP BY ou.[OrganizationId], o.[Name]
            ORDER BY COUNT(*) DESC;

            -- Attempts that cannot be attributed to any organization
            -- (unknown user or user without an active membership)
            SELECT
                ISNULL(SUM(CASE WHEN la.[IsSuccessful] = 1 THEN 1 ELSE 0 END), 0) AS SuccessCount,
                ISNULL(SUM(CASE WHEN la.[IsSuccessful] = 0 THEN 1 ELSE 0 END), 0) AS FailureCount
            FROM [dbo].[LoginAttempts] la
            WHERE la.[AttemptedAt] >= @From
              AND NOT EXISTS (
                  SELECT 1 FROM [dbo].[OrganizationUsers] ou
                  WHERE ou.[UserId] = la.[UserId] AND ou.[IsActive] = 1);",
            new { Days = days, TimeZone = sqlTimeZone });

        var loginsPerDay = (await grid.ReadAsync<DailyLoginCount>()).ToList();
        var activeUsersPerDay = (await grid.ReadAsync<DailyCount>()).ToList();
        var activeUsersInWindow = await grid.ReadSingleAsync<int>();
        var failureReasons = (await grid.ReadAsync<ReasonCount>()).ToList();
        var windowTotals = await grid.ReadSingleAsync<WindowTotalsRow>();
        var lockedOutNow = await grid.ReadSingleAsync<int>();
        var lockoutEvents = await grid.ReadSingleAsync<int>();
        var topFailingIps = (await grid.ReadAsync<IpFailureCount>()).ToList();
        var loginsByApplication = (await grid.ReadAsync<ApplicationLoginCount>()).ToList();
        var loginsByOrganization = (await grid.ReadAsync<OrganizationLoginCount>()).ToList();
        var unattributed = await grid.ReadSingleAsync<UnattributedLoginsRow>();

        if (unattributed.SuccessCount > 0 || unattributed.FailureCount > 0)
        {
            loginsByOrganization.Add(new OrganizationLoginCount(
                null, null, unattributed.SuccessCount, unattributed.FailureCount));
        }

        return new AuthStatsSnapshot
        {
            LoginsPerDay = loginsPerDay,
            ActiveUsersPerDay = activeUsersPerDay,
            ActiveUsersInWindow = activeUsersInWindow,
            FailureReasons = failureReasons,
            WindowSuccessCount = windowTotals.WindowSuccessCount,
            WindowFailureCount = windowTotals.WindowFailureCount,
            PreviousWindowSuccessCount = windowTotals.PreviousWindowSuccessCount,
            PreviousWindowFailureCount = windowTotals.PreviousWindowFailureCount,
            LockedOutNow = lockedOutNow,
            LockoutEventsInWindow = lockoutEvents,
            TopFailingIps = topFailingIps,
            LoginsByApplication = loginsByApplication,
            LoginsByOrganization = loginsByOrganization
        };
    }

    /// <inheritdoc />
    public async Task<AuditStatsSnapshot> GetAuditStatsAsync(
        int days,
        string timeZone,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sqlTimeZone = ToSqlServerTimeZone(timeZone);

        // Only columns the AuditLogs table actually has are grouped on: there is no
        // outcome column and no action-type column, so no success/failure split is
        // possible here and none is implied.
        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();
            DECLARE @From DATETIME2 = DATEADD(DAY, -@Days, @Now);
            DECLARE @PrevFrom DATETIME2 = DATEADD(DAY, -@Days, @From);

            -- Current and previous window totals (for the trend delta)
            SELECT
                ISNULL(SUM(CASE WHEN [Timestamp] >= @From THEN 1 ELSE 0 END), 0) AS TotalInWindow,
                ISNULL(SUM(CASE WHEN [Timestamp] < @From THEN 1 ELSE 0 END), 0) AS PreviousWindowTotal
            FROM [dbo].[AuditLogs]
            WHERE [Timestamp] >= @PrevFrom;

            -- Events per viewer-local calendar day inside the UTC instant window
            SELECT CAST(([Timestamp] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE) AS [Date],
                   COUNT(*) AS [Count]
            FROM [dbo].[AuditLogs]
            WHERE [Timestamp] >= @From
            GROUP BY CAST(([Timestamp] AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone AS DATE)
            ORDER BY [Date];

            -- Actions, most frequent first
            SELECT [Action] AS Reason, COUNT(*) AS [Count]
            FROM [dbo].[AuditLogs]
            WHERE [Timestamp] >= @From
            GROUP BY [Action]
            ORDER BY [Count] DESC;

            -- Affected entity types, most frequent first
            SELECT ISNULL([EntityType], N'unknown') AS Reason, COUNT(*) AS [Count]
            FROM [dbo].[AuditLogs]
            WHERE [Timestamp] >= @From
            GROUP BY ISNULL([EntityType], N'unknown')
            ORDER BY [Count] DESC;",
            new { Days = days, TimeZone = sqlTimeZone });

        var windowTotals = await grid.ReadSingleAsync<AuditWindowTotalsRow>();
        var eventsPerDay = (await grid.ReadAsync<DailyCount>()).ToList();
        var topActions = (await grid.ReadAsync<ReasonCount>()).ToList();
        var byEntityType = (await grid.ReadAsync<ReasonCount>()).ToList();

        return new AuditStatsSnapshot
        {
            TotalInWindow = windowTotals.TotalInWindow,
            PreviousWindowTotal = windowTotals.PreviousWindowTotal,
            EventsPerDay = eventsPerDay,
            TopActions = topActions,
            ByEntityType = byEntityType
        };
    }

    /// <inheritdoc />
    public async Task<SessionStatsSnapshot> GetSessionStatsAsync(int days, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();
            DECLARE @From DATETIME2 = DATEADD(DAY, -@Days, @Now);

            -- Session state counts
            SELECT
                ISNULL(SUM(CASE WHEN [EndedAt] IS NULL AND [ExpiresAt] > @Now THEN 1 ELSE 0 END), 0) AS ActiveSessions,
                ISNULL(SUM(CASE WHEN [EndedAt] IS NULL AND [ExpiresAt] <= @Now THEN 1 ELSE 0 END), 0) AS StaleOpenSessions,
                ISNULL(SUM(CASE WHEN [StartedAt] >= @From THEN 1 ELSE 0 END), 0) AS StartedInWindow
            FROM [dbo].[UserSessions];

            -- End reasons of sessions ended inside the window
            SELECT ISNULL([EndReason], N'unknown') AS Reason, COUNT(*) AS [Count]
            FROM [dbo].[UserSessions]
            WHERE [EndedAt] >= @From
            GROUP BY ISNULL([EndReason], N'unknown')
            ORDER BY [Count] DESC;

            -- Average duration of sessions ended inside the window
            SELECT AVG(CAST(DATEDIFF(SECOND, [StartedAt], [EndedAt]) AS FLOAT)) / 60.0
            FROM [dbo].[UserSessions]
            WHERE [EndedAt] >= @From;

            -- Refresh-token state counts
            SELECT
                ISNULL(SUM(CASE WHEN [RevokedAt] IS NULL AND [ExpiresAt] > @Now THEN 1 ELSE 0 END), 0) AS ActiveRefreshTokens,
                ISNULL(SUM(CASE WHEN [RevokedAt] >= @From THEN 1 ELSE 0 END), 0) AS TokensRevokedInWindow,
                ISNULL(SUM(CASE WHEN [RevokedAt] IS NULL AND [ExpiresAt] > @Now AND [ExpiresAt] <= DATEADD(DAY, 7, @Now) THEN 1 ELSE 0 END), 0) AS TokensExpiringIn7Days
            FROM [dbo].[RefreshTokens];

            -- Revocation reasons inside the window
            SELECT ISNULL([ReasonRevoked], N'unknown') AS Reason, COUNT(*) AS [Count]
            FROM [dbo].[RefreshTokens]
            WHERE [RevokedAt] >= @From
            GROUP BY ISNULL([ReasonRevoked], N'unknown')
            ORDER BY [Count] DESC;",
            new { Days = days });

        var sessions = await grid.ReadSingleAsync<SessionTotalsRow>();
        var endReasons = (await grid.ReadAsync<ReasonCount>()).ToList();
        var averageSessionMinutes = await grid.ReadSingleAsync<double?>();
        var tokens = await grid.ReadSingleAsync<TokenTotalsRow>();
        var revocationReasons = (await grid.ReadAsync<ReasonCount>()).ToList();

        return new SessionStatsSnapshot
        {
            ActiveSessions = sessions.ActiveSessions,
            StaleOpenSessions = sessions.StaleOpenSessions,
            StartedInWindow = sessions.StartedInWindow,
            EndReasons = endReasons,
            AverageSessionMinutes = averageSessionMinutes,
            ActiveRefreshTokens = tokens.ActiveRefreshTokens,
            TokensRevokedInWindow = tokens.TokensRevokedInWindow,
            RevocationReasons = revocationReasons,
            TokensExpiringIn7Days = tokens.TokensExpiringIn7Days
        };
    }

    /// <inheritdoc />
    public async Task<AppActivitySnapshot> GetAppActivityAsync(int days, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();
            DECLARE @From DATETIME2 = DATEADD(DAY, -@Days, @Now);

            -- Activity per registered application (zero-activity applications included)
            SELECT a.[Id] AS ApplicationId,
                   a.[Name] AS ApplicationName,
                   a.[IsActive],
                   ISNULL(la.SuccessfulLogins, 0) AS SuccessfulLogins,
                   ISNULL(la.DistinctUsers, 0) AS DistinctUsers,
                   ISNULL(us.ActiveSessions, 0) AS ActiveSessions
            FROM [dbo].[Applications] a
            LEFT JOIN (
                SELECT [ApplicationId],
                       SUM(CASE WHEN [IsSuccessful] = 1 THEN 1 ELSE 0 END) AS SuccessfulLogins,
                       COUNT(DISTINCT CASE WHEN [IsSuccessful] = 1 THEN [UserId] END) AS DistinctUsers
                FROM [dbo].[LoginAttempts]
                WHERE [AttemptedAt] >= @From
                GROUP BY [ApplicationId]
            ) la ON la.[ApplicationId] = a.[Id]
            LEFT JOIN (
                SELECT [ApplicationId], COUNT(*) AS ActiveSessions
                FROM [dbo].[UserSessions]
                WHERE [EndedAt] IS NULL AND [ExpiresAt] > @Now
                GROUP BY [ApplicationId]
            ) us ON us.[ApplicationId] = a.[Id]
            WHERE a.[IsDeleted] = 0
            ORDER BY ISNULL(la.SuccessfulLogins, 0) DESC, a.[Name];

            -- Activity carrying no application context
            SELECT
                ISNULL(SUM(CASE WHEN [IsSuccessful] = 1 THEN 1 ELSE 0 END), 0) AS SuccessfulLogins,
                COUNT(DISTINCT CASE WHEN [IsSuccessful] = 1 THEN [UserId] END) AS DistinctUsers
            FROM [dbo].[LoginAttempts]
            WHERE [AttemptedAt] >= @From AND [ApplicationId] IS NULL;

            SELECT COUNT(*)
            FROM [dbo].[UserSessions]
            WHERE [ApplicationId] IS NULL AND [EndedAt] IS NULL AND [ExpiresAt] > @Now;

            -- Enablement matrix limited to the ten largest organizations by membership
            SELECT oa.[OrganizationId],
                   o.[Name] AS OrganizationName,
                   oa.[ApplicationId],
                   a.[Name] AS ApplicationName,
                   oa.[SubscriptionTier],
                   oa.[ExpiresAt]
            FROM [dbo].[OrganizationApplications] oa
            JOIN (
                SELECT TOP (10) o.[Id], o.[Name]
                FROM [dbo].[Organizations] o
                LEFT JOIN [dbo].[OrganizationUsers] ou
                    ON ou.[OrganizationId] = o.[Id] AND ou.[IsActive] = 1
                WHERE o.[IsActive] = 1
                GROUP BY o.[Id], o.[Name]
                ORDER BY COUNT(ou.[Id]) DESC
            ) o ON o.[Id] = oa.[OrganizationId]
            JOIN [dbo].[Applications] a ON a.[Id] = oa.[ApplicationId]
            WHERE oa.[IsActive] = 1
            ORDER BY o.[Name], a.[Name];",
            new { Days = days });

        var applications = (await grid.ReadAsync<ApplicationActivity>()).ToList();
        var unknownApp = await grid.ReadSingleAsync<UnknownAppActivityRow>();
        var unknownAppSessions = await grid.ReadSingleAsync<int>();
        var organizationApplications = (await grid.ReadAsync<OrganizationApplicationEnablement>()).ToList();

        if (unknownApp.SuccessfulLogins > 0 || unknownApp.DistinctUsers > 0 || unknownAppSessions > 0)
        {
            applications.Add(new ApplicationActivity(
                null, null, true, unknownApp.SuccessfulLogins, unknownApp.DistinctUsers, unknownAppSessions));
        }

        return new AppActivitySnapshot
        {
            Applications = applications,
            OrganizationApplications = organizationApplications
        };
    }

    private record UserTotalsRow
    {
        public int TotalUsers { get; init; }
        public int ActiveUsers { get; init; }
        public int MfaEnabled { get; init; }
        public int NewInWindow { get; init; }
        public int CohortEmailConfirmed { get; init; }
        public int CohortLoggedIn { get; init; }
        public int DormantOver30Days { get; init; }
        public int DormantOver60Days { get; init; }
        public int DormantOver90Days { get; init; }
        public int NeverLoggedIn { get; init; }
    }

    private record WindowTotalsRow
    {
        public int WindowSuccessCount { get; init; }
        public int WindowFailureCount { get; init; }
        public int PreviousWindowSuccessCount { get; init; }
        public int PreviousWindowFailureCount { get; init; }
    }

    private record UnattributedLoginsRow
    {
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
    }

    private record SessionTotalsRow
    {
        public int ActiveSessions { get; init; }
        public int StaleOpenSessions { get; init; }
        public int StartedInWindow { get; init; }
    }

    private record TokenTotalsRow
    {
        public int ActiveRefreshTokens { get; init; }
        public int TokensRevokedInWindow { get; init; }
        public int TokensExpiringIn7Days { get; init; }
    }

    private record UnknownAppActivityRow
    {
        public int SuccessfulLogins { get; init; }
        public int DistinctUsers { get; init; }
    }

    private record AuditWindowTotalsRow
    {
        public int TotalInWindow { get; init; }
        public int PreviousWindowTotal { get; init; }
    }

    /// <summary>
    /// SQL Server uses Windows time-zone identifiers even though API clients
    /// and stored user preferences use portable IANA identifiers.
    /// </summary>
    private static string ToSqlServerTimeZone(string timeZone)
    {
        if (string.Equals(timeZone, "UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(timeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out var windowsId))
        {
            return windowsId;
        }

        throw new InvalidOperationException(
            $"The validated IANA time zone '{timeZone}' cannot be mapped for SQL Server.");
    }
}
