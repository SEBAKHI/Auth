using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the IdP (SSO) session repository.
/// </summary>
public class IdpSessionRepository : IIdpSessionRepository
{
    // IdpSessions.sql: [DeviceId] NVARCHAR(64).
    private const int DeviceIdMaxLength = 64;

    private readonly IDbConnectionFactory _connectionFactory;

    public IdpSessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IdpSession> CreateAsync(IdpSession session, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[IdpSessions] (
                [Id], [UserId], [TokenHash], [ExpiresAt], [RevokedAt],
                [CreatedAt], [IpAddress], [DeviceInfo], [AuthMethods],
                [IsPersistent], [LastActivityAt], [DeviceId]
            ) VALUES (
                @Id, @UserId, @TokenHash, @ExpiresAt, @RevokedAt,
                @CreatedAt, @IpAddress, @DeviceInfo, @AuthMethods,
                @IsPersistent, @LastActivityAt, @DeviceId
            )",
            new
            {
                session.Id,
                session.UserId,
                session.TokenHash,
                session.ExpiresAt,
                session.RevokedAt,
                session.CreatedAt,
                session.IpAddress,
                session.DeviceInfo,
                session.AuthMethods,
                session.IsPersistent,
                session.LastActivityAt,
                // OI-178. Bounded at the edge too (GetDeviceId); cut here as well, because a
                // failed insert would cost the person their single sign-on without a word.
                DeviceId = ColumnText.Truncate(session.DeviceId, DeviceIdMaxLength)
            });

        return session;
    }

    /// <inheritdoc />
    public async Task<IdpSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<IdpSessionDto>(@"
            SELECT
                [Id], [UserId], [TokenHash], [ExpiresAt], [RevokedAt],
                [CreatedAt], [IpAddress], [DeviceInfo],
                [IsPersistent], [LastActivityAt], [DeviceId]
            FROM [dbo].[IdpSessions]
            WHERE [TokenHash] = @TokenHash
              AND [RevokedAt] IS NULL",
            new { TokenHash = tokenHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IdpSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<IdpSessionDto>(@"
            SELECT
                [Id], [UserId], [TokenHash], [ExpiresAt], [RevokedAt],
                [CreatedAt], [IpAddress], [DeviceInfo],
                [IsPersistent], [LastActivityAt], [DeviceId]
            FROM [dbo].[IdpSessions]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IdpSession session, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[IdpSessions] SET
                [RevokedAt] = @RevokedAt
            WHERE [Id] = @Id",
            new { session.Id, session.RevokedAt });
    }

    /// <inheritdoc />
    public async Task<bool> RecordActivityAsync(
        Guid sessionId, DateTime nowUtc, TimeSpan idleLimit, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, and every condition of the session's validity in it
        // (IdpSession.IsValidAt): the read that came before may be stale, and a
        // session that died idle is revoked only lazily, so a write without them
        // would bring a dead session back. NULL IsPersistent is a browser session.
        var idleMinutes = (int)idleLimit.TotalMinutes;
        var recorded = await connection.ExecuteAsync(@"
            UPDATE [dbo].[IdpSessions] SET
                [LastActivityAt] = @Now
            WHERE [Id] = @Id
              AND [RevokedAt] IS NULL
              AND [ExpiresAt] > @Now
              AND ([IsPersistent] = 1
                   OR @IdleMinutes = 0
                   OR COALESCE([LastActivityAt], [CreatedAt]) > @IdleCutoff)",
            new
            {
                Id = sessionId,
                Now = nowUtc,
                IdleMinutes = idleMinutes,
                IdleCutoff = nowUtc.AddMinutes(-idleMinutes)
            });

        return recorded == 1;
    }

    /// <inheritdoc />
    public async Task<int> EndIdleBrowserSessionsAsync(
        DateTime nowUtc, DateTime lastUsedAtOrBefore, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The complement of the activity write's idle condition above, for every
        // browser session at once. NULL IsPersistent is a browser session.
        return await connection.ExecuteAsync(@"
            UPDATE [dbo].[IdpSessions] SET
                [RevokedAt] = @Now
            WHERE [RevokedAt] IS NULL
              AND ISNULL([IsPersistent], 0) = 0
              AND [ExpiresAt] > @Now
              AND COALESCE([LastActivityAt], [CreatedAt]) <= @IdleCutoff",
            new { Now = nowUtc, IdleCutoff = lastUsedAtOrBefore });
    }

    /// <inheritdoc />
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return RevokeAllForUserExceptAsync(userId, exceptTokenHash: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllForUserExceptAsync(
        Guid userId,
        string? exceptTokenHash,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The hash comparison is a plain equality test on a value this server
        // computed itself (HMAC-SHA256 of the cookie token), so there is nothing
        // attacker-controlled to time: a caller who guesses wrong simply loses
        // the session they were trying to keep.
        return await connection.ExecuteAsync(@"
            UPDATE [dbo].[IdpSessions] SET
                [RevokedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [RevokedAt] IS NULL
              AND (@ExceptTokenHash IS NULL OR [TokenHash] <> @ExceptTokenHash)",
            new { UserId = userId, ExceptTokenHash = exceptTokenHash });
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[IdpSessions]
            WHERE [RevokedAt] IS NOT NULL AND [RevokedAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        deleted += await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[IdpSessions]
            WHERE [RevokedAt] IS NULL AND [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    // Internal DTO for mapping from database
    private record IdpSessionDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? IpAddress { get; init; }
        public string? DeviceInfo { get; init; }
        public bool? IsPersistent { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public string? DeviceId { get; init; }

        // NULL IsPersistent is a row recorded before the column existed: a
        // browser session, so it idles out like a new unticked sign-in.
        public IdpSession ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            CreatedAt,
            ExpiresAt,
            RevokedAt,
            IpAddress,
            DeviceInfo,
            IsPersistent ?? false,
            LastActivityAt,
            DeviceId);
    }
}
