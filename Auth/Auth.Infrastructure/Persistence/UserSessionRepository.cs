using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the user session repository.
/// </summary>
public class UserSessionRepository : IUserSessionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    // Mirrors the column widths in UserSessions.sql.
    private const int UserAgentMaxLength = 500;
    private const int DeviceNameMaxLength = 100;
    private const int DeviceIdMaxLength = 64;

    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;

    // SQL SELECT clause with column aliases for direct entity mapping.
    // Only genuinely differently-named columns are aliased; every device column
    // maps to the property of the same name. [DeviceType] used to be aliased to
    // [DeviceName], which made the entity's DeviceName always null and its
    // DeviceType unreadable — two properties fed by one column that held neither.
    private const string SelectColumns = @"
        [Id],
        [UserId],
        [ApplicationId],
        [SessionToken] AS [SessionTokenHash],
        [IpAddress],
        [UserAgent],
        -- Rows written before the column was populated hold NULL, and the entity
        -- maps this to a non-nullable enum; 'unknown' is the member that means
        -- exactly that.
        COALESCE([DeviceType], 'unknown') AS [DeviceType],
        [DeviceName],
        [DeviceId],
        [DeviceHash],
        [Location],
        [StartedAt] AS [CreatedAt],
        [LastActivityAt],
        [ExpiresAt],
        [EndedAt] AS [TerminatedAt],
        [EndReason] AS [TerminationReason],
        CAST(CASE WHEN [EndedAt] IS NULL THEN 1 ELSE 0 END AS BIT) AS [IsActive]";

    // The same projection against OUTPUT's post-update image. An OUTPUT clause
    // cannot use unqualified column names, so this cannot simply reuse
    // SelectColumns. Reading `inserted` rather than `deleted` is what makes the
    // returned entities describe the session as it now is — ended, with the
    // reason set — instead of the live row the statement just replaced.
    private const string OutputInsertedColumns = @"
        inserted.[Id],
        inserted.[UserId],
        inserted.[ApplicationId],
        inserted.[SessionToken] AS [SessionTokenHash],
        inserted.[IpAddress],
        inserted.[UserAgent],
        COALESCE(inserted.[DeviceType], 'unknown') AS [DeviceType],
        inserted.[DeviceName],
        inserted.[DeviceId],
        inserted.[DeviceHash],
        inserted.[Location],
        inserted.[StartedAt] AS [CreatedAt],
        inserted.[LastActivityAt],
        inserted.[ExpiresAt],
        inserted.[EndedAt] AS [TerminatedAt],
        inserted.[EndReason] AS [TerminationReason],
        CAST(CASE WHEN inserted.[EndedAt] IS NULL THEN 1 ELSE 0 END AS BIT) AS [IsActive]";

    public UserSessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [SessionToken] = @SessionToken",
            new { SessionToken = tokenHash });
    }

    /// <inheritdoc />
    public async Task<UserSession> CreateAsync(UserSession session, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Map entity properties to database column names
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserSessions] (
                [Id], [UserId], [ApplicationId], [SessionToken],
                [IpAddress], [UserAgent], [DeviceType], [Location],
                [StartedAt], [LastActivityAt], [ExpiresAt],
                [EndedAt], [EndReason],
                [DeviceName], [DeviceId], [DeviceHash]
            ) VALUES (
                @Id, @UserId, @ApplicationId, @SessionTokenHash,
                @IpAddress, @UserAgent, @DeviceType, @Location,
                @CreatedAt, @LastActivityAt, @ExpiresAt,
                @TerminatedAt, @TerminationReason,
                @DeviceName, @DeviceId, @DeviceHash
            )",
            new
            {
                session.Id,
                session.UserId,
                session.ApplicationId,
                session.SessionTokenHash,
                session.IpAddress,
                // A user agent longer than the column costs the whole row: the
                // insert throws and the caller's catch swallows it, leaving a
                // signed-in user with no session to manage. Truncating loses the
                // tail of a string we only ever parse the head of.
                UserAgent = Truncate(session.UserAgent, UserAgentMaxLength),
                // Dapper sends an enum as its numeric value; the column holds the
                // documented lowercase vocabulary, which is what the member names
                // lowercase to.
                DeviceType = session.DeviceType.ToString().ToLowerInvariant(),
                session.Location,
                session.CreatedAt,
                session.LastActivityAt,
                session.ExpiresAt,
                session.TerminatedAt,
                session.TerminationReason,
                DeviceName = Truncate(session.DeviceName, DeviceNameMaxLength),
                DeviceId = Truncate(session.DeviceId, DeviceIdMaxLength),
                session.DeviceHash
            });

        return session;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(UserSession session, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Map entity properties to database column names
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [LastActivityAt] = @LastActivityAt,
                [ExpiresAt] = @ExpiresAt,
                [EndedAt] = @TerminatedAt,
                [EndReason] = @TerminationReason
            WHERE [Id] = @Id",
            new
            {
                session.Id,
                session.LastActivityAt,
                session.ExpiresAt,
                session.TerminatedAt,
                session.TerminationReason
            });
    }

    // Column names are the real table columns ([StartedAt]), not the SELECT
    // aliases, to stay unambiguous.
    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.Sessions.CreatedAt, ["[StartedAt]"]),
        (SortFields.Sessions.LastActivityAt, ["[LastActivityAt]"]),
        (SortFields.Sessions.ExpiresAt, ["[ExpiresAt]"]),
        (SortFields.Sessions.IpAddress, ["[IpAddress]"]),
        (SortFields.Sessions.UserAgent, ["[UserAgent]"]),
        (SortFields.Sessions.DeviceName, ["[DeviceName]"]),
        (SortFields.Sessions.Location, ["[Location]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsForUserAsync(
        Guid userId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(
            SortColumns, sortBy, sortDirection, "[LastActivityAt] DESC", "[Id]");
        var sessions = await connection.QueryAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [UserId] = @UserId
              AND [EndedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
            ORDER BY {orderBy}",
            new { UserId = userId });

        return sessions.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> GetActiveByDeviceHashAsync(
        Guid userId,
        string deviceHash,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var sessions = await connection.QueryAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [UserId] = @UserId
              AND [DeviceHash] = @DeviceHash
              AND [EndedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()",
            new { UserId = userId, DeviceHash = deviceHash });

        return sessions.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<int> CountActiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Same predicate as GetActiveSessionsForUserAsync, served by the filtered
        // IX_UserSessions_UserId. Counting in SQL rather than materialising the
        // list keeps the deny check off the row-mapping path it never needs.
        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM [dbo].[UserSessions]
            WHERE [UserId] = @UserId
              AND [EndedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> TerminateBeyondLimitAsync(
        Guid userId,
        int keepNewest,
        string reason,
        CancellationToken cancellationToken)
    {
        if (keepNewest <= 0)
        {
            // 0 is "unlimited", and a negative rank window would end every
            // session the user has — the caller's misconfiguration must not
            // become a mass sign-out.
            return [];
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, not a read followed by a write. Two sign-ins landing
        // together each insert their row and then run this; the row lock decides
        // which one ends a given session, and OUTPUT reports only the rows this
        // execution actually changed. That is what stops the same eviction being
        // counted — and emailed — twice.
        //
        // [Id] breaks ties so the ranking is deterministic when two sessions
        // share a LastActivityAt to the tick, which they do when a client opens
        // two of them in the same request.
        var evicted = await connection.QueryAsync<UserSession>($@"
            WITH [ranked] AS (
                SELECT
                    [Id],
                    ROW_NUMBER() OVER (
                        ORDER BY [LastActivityAt] DESC, [StartedAt] DESC, [Id]) AS [rn]
                FROM [dbo].[UserSessions]
                WHERE [UserId] = @UserId
                  AND [EndedAt] IS NULL
                  AND [ExpiresAt] > GETUTCDATE()
            )
            UPDATE [s] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            OUTPUT {OutputInsertedColumns}
            FROM [dbo].[UserSessions] [s]
            INNER JOIN [ranked] [r] ON [r].[Id] = [s].[Id]
            WHERE [r].[rn] > @KeepNewest
              AND [s].[EndedAt] IS NULL",
            new { UserId = userId, KeepNewest = keepNewest, Reason = reason });

        return evicted.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task TerminateAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            WHERE [UserId] = @UserId
              AND [EndedAt] IS NULL",
            new { UserId = userId, Reason = reason });
    }

    /// <inheritdoc />
    public async Task TerminateOtherSessionsAsync(
        Guid userId,
        Guid exceptSessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            WHERE [UserId] = @UserId
              AND [Id] <> @ExceptSessionId
              AND [EndedAt] IS NULL",
            new { UserId = userId, ExceptSessionId = exceptSessionId, Reason = reason });
    }

    /// <inheritdoc />
    public async Task TerminateAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            WHERE [Id] = @SessionId
              AND [EndedAt] IS NULL",
            new { SessionId = sessionId, Reason = reason });
    }

    /// <inheritdoc />
    public async Task TerminateForApplicationAsync(
        Guid applicationId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Only the OAuth token endpoint stamps ApplicationId on a session, so
        // platform sessions (null) are left alone by construction.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            WHERE [ApplicationId] = @ApplicationId
              AND [EndedAt] IS NULL",
            new { ApplicationId = applicationId, Reason = reason });
    }

    /// <inheritdoc />
    public async Task TerminateForUserAndApplicationAsync(
        Guid userId,
        Guid applicationId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = @Reason
            WHERE [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [EndedAt] IS NULL",
            new { UserId = userId, ApplicationId = applicationId, Reason = reason });
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserSessions] SET
                [EndedAt] = GETUTCDATE(),
                [EndReason] = 'timeout'
            WHERE [EndedAt] IS NULL
              AND [ExpiresAt] < GETUTCDATE()");
    }
}
