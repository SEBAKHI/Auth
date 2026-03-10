using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the user session repository.
/// </summary>
public class UserSessionRepository : IUserSessionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    // SQL SELECT clause with column aliases for direct entity mapping
    // Database columns are aliased to match C# entity property names
    private const string SelectColumns = @"
        [Id],
        [UserId],
        [ApplicationId],
        [SessionToken] AS [SessionTokenHash],
        [IpAddress],
        [UserAgent],
        [DeviceType] AS [DeviceName],
        [Location],
        [StartedAt] AS [CreatedAt],
        [LastActivityAt],
        [ExpiresAt],
        [EndedAt] AS [TerminatedAt],
        [EndReason] AS [TerminationReason],
        CAST(CASE WHEN [EndedAt] IS NULL THEN 1 ELSE 0 END AS BIT) AS [IsActive]";

    public UserSessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<UserSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [SessionToken] = @SessionToken",
            new { SessionToken = tokenHash });
    }

    /// <inheritdoc />
    public async Task<UserSession> CreateAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Map entity properties to database column names
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserSessions] (
                [Id], [UserId], [ApplicationId], [SessionToken],
                [IpAddress], [UserAgent], [DeviceType], [Location],
                [StartedAt], [LastActivityAt], [ExpiresAt],
                [EndedAt], [EndReason]
            ) VALUES (
                @Id, @UserId, @ApplicationId, @SessionTokenHash,
                @IpAddress, @UserAgent, @DeviceName, @Location,
                @CreatedAt, @LastActivityAt, @ExpiresAt,
                @TerminatedAt, @TerminationReason
            )",
            new
            {
                session.Id,
                session.UserId,
                session.ApplicationId,
                session.SessionTokenHash,
                session.IpAddress,
                session.UserAgent,
                DeviceName = session.DeviceName ?? session.DeviceId,
                session.Location,
                session.CreatedAt,
                session.LastActivityAt,
                session.ExpiresAt,
                session.TerminatedAt,
                session.TerminationReason
            });

        return session;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default)
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var sessions = await connection.QueryAsync<UserSession>($@"
            SELECT {SelectColumns}
            FROM [dbo].[UserSessions]
            WHERE [UserId] = @UserId
              AND [EndedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
            ORDER BY [LastActivityAt] DESC",
            new { UserId = userId });

        return sessions.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task TerminateAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
    public async Task TerminateAsync(Guid sessionId, string reason, CancellationToken cancellationToken = default)
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
    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
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
