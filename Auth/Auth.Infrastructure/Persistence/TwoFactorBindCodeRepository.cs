using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the repository for the codes emailed before an
/// account binds its first second factor.
/// </summary>
public class TwoFactorBindCodeRepository : ITwoFactorBindCodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TwoFactorBindCodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TwoFactorBindCode?> GetLiveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // A read only, to find the row: whether it may still be checked is decided
        // again by the reservation, in the statement that counts the attempt.
        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorBindCodeDto>(new CommandDefinition(@"
            SELECT TOP 1
                [Id], [UserId], [CodeHash], [ExpiresAt], [UsedAt], [AttemptCount], [IpAddress], [CreatedAt]
            FROM [dbo].[TwoFactorBindCodes]
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > SYSUTCDATETIME()
            ORDER BY [CreatedAt] DESC",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(TwoFactorBindCode code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO [dbo].[TwoFactorBindCodes] (
                [Id], [UserId], [CodeHash], [ExpiresAt], [UsedAt], [AttemptCount], [IpAddress], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @CodeHash, @ExpiresAt, @UsedAt, @AttemptCount, @IpAddress, @CreatedAt
            )",
            new
            {
                code.Id,
                code.UserId,
                code.CodeHash,
                code.ExpiresAt,
                code.UsedAt,
                code.AttemptCount,
                code.IpAddress,
                code.CreatedAt
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The code is consumed by the transaction that switches the factor on
    /// (<see cref="TwoFactorStateStore"/>), never on its own.
    /// </remarks>
    public Task<int?> TryReserveAttemptAsync(Guid codeId, int maxAttempts, CancellationToken cancellationToken) =>
        SingleUseCodeStatements.TwoFactorBindCodes.TryReserveAsync(
            _connectionFactory, codeId, maxAttempts, cancellationToken);

    /// <inheritdoc />
    public async Task InvalidateOutstandingForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Closed by stamping UsedAt: a superseded code and a spent one are both
        // unusable, and one column carries "no longer live" for the whole table.
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[TwoFactorBindCodes] SET
                [UsedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL",
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> GetRecentCountForUserAsync(Guid userId, TimeSpan window, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
            SELECT COUNT(1) FROM [dbo].[TwoFactorBindCodes]
            WHERE [UserId] = @UserId
              AND [CreatedAt] > DATEADD(SECOND, -@WindowSeconds, GETUTCDATE())",
            new { UserId = userId, WindowSeconds = (int)window.TotalSeconds },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(@"
            DELETE TOP (@BatchSize) FROM [dbo].[TwoFactorBindCodes]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize },
            cancellationToken: cancellationToken));
    }

    // Internal DTO for mapping from database
    private record TwoFactorBindCodeDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string CodeHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public string? IpAddress { get; init; }
        public DateTime CreatedAt { get; init; }

        public TwoFactorBindCode ToEntity() => new(
            Id,
            UserId,
            CodeHash,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            IpAddress,
            CreatedAt);
    }
}
