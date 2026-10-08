using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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
            SELECT [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt], [PrimaryMethod]
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
                [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt], [PrimaryMethod]
            ) VALUES (
                @Id, @UserId, @TokenHash, @IpAddress, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt, @PrimaryMethod
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
                challenge.CreatedAt,
                PrimaryMethod = challenge.PrimaryMethod.ToStored()
            });
    }

    /// <inheritdoc />
    /// <remarks>
    /// The challenge is consumed by the sign-in commit, inside the transaction
    /// that also settles the factor (<see cref="TwoFactorStateStore"/>), never
    /// on its own.
    /// </remarks>
    public Task<int?> TryReserveAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken) =>
        SingleUseCodeStatements.TwoFactorChallenges.TryReserveAsync(
            _connectionFactory, challengeId, maxAttempts, cancellationToken);

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
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[TwoFactorChallenges]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
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
        public int? PrimaryMethod { get; init; }

        public TwoFactorChallenge ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            IpAddress,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            CreatedAt,
            AuthenticationMethods.FromStored(PrimaryMethod));
    }
}
