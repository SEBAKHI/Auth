using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the two-factor challenge repository.
/// </summary>
public class TwoFactorChallengeRepository : ITwoFactorChallengeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TwoFactorChallengeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TwoFactorChallenge?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorChallengeDto>(@"
            SELECT [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            FROM [dbo].[TwoFactorChallenges]
            WHERE [TokenHash] = @TokenHash",
            new { TokenHash = tokenHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[TwoFactorChallenges] (
                [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @TokenHash, @IpAddress, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt
            )",
            new
            {
                challenge.Id,
                challenge.UserId,
                challenge.TokenHash,
                challenge.IpAddress,
                challenge.ExpiresAt,
                challenge.UsedAt,
                challenge.AttemptCount,
                challenge.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task<bool> MarkAsUsedAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The WHERE clause was already the whole race condition's answer; the
        // rows-affected was simply thrown away, which made the guard decorative.
        // Returning it turns this statement into the compare-and-set it looks like.
        var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [UsedAt] = GETUTCDATE()
            WHERE [Id] = @ChallengeId
              AND [UsedAt] IS NULL",
            new { ChallengeId = challengeId });

        return rowsAffected > 0;
    }

    /// <inheritdoc />
    public async Task IncrementAttemptCountAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [AttemptCount] = [AttemptCount] + 1
            WHERE [Id] = @ChallengeId",
            new { ChallengeId = challengeId });
    }

    /// <inheritdoc />
    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [UsedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Delete challenges that expired more than 7 days ago
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[TwoFactorChallenges]
            WHERE [ExpiresAt] < DATEADD(DAY, -7, GETUTCDATE())");
    }

    // Internal DTO for mapping from database
    private record TwoFactorChallengeDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public string? IpAddress { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public DateTime CreatedAt { get; init; }

        public TwoFactorChallenge ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            IpAddress,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            CreatedAt);
    }
}
