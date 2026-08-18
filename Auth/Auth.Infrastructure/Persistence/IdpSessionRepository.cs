using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the IdP (SSO) session repository.
/// </summary>
public class IdpSessionRepository : IIdpSessionRepository
{
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
                [CreatedAt], [IpAddress], [DeviceInfo]
            ) VALUES (
                @Id, @UserId, @TokenHash, @ExpiresAt, @RevokedAt,
                @CreatedAt, @IpAddress, @DeviceInfo
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
                session.DeviceInfo
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
                [CreatedAt], [IpAddress], [DeviceInfo]
            FROM [dbo].[IdpSessions]
            WHERE [TokenHash] = @TokenHash
              AND [RevokedAt] IS NULL",
            new { TokenHash = tokenHash });

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

        public IdpSession ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            CreatedAt,
            ExpiresAt,
            RevokedAt,
            IpAddress,
            DeviceInfo);
    }
}
