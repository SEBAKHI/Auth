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
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[IdpSessions] SET
                [RevokedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [RevokedAt] IS NULL",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[IdpSessions]
            WHERE ([ExpiresAt] < @OlderThan)
               OR ([RevokedAt] IS NOT NULL AND [RevokedAt] < @OlderThan)",
            new { OlderThan = olderThan });
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
