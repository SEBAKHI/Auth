using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the password history repository.
/// </summary>
public class PasswordHistoryRepository : IPasswordHistoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PasswordHistoryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(PasswordHistory history, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PasswordHistory] (
                [Id], [UserId], [PasswordHash], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @PasswordHash, @CreatedAt
            )",
            new
            {
                history.Id,
                history.UserId,
                history.PasswordHash,
                history.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecentHashesAsync(
        Guid userId,
        int count,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var hashes = await connection.QueryAsync<string>(@"
            SELECT TOP (@Count) [PasswordHash]
            FROM [dbo].[PasswordHistory]
            WHERE [UserId] = @UserId
            ORDER BY [CreatedAt] DESC",
            new { UserId = userId, Count = count });

        return hashes.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task CleanupOldHistoryAsync(
        Guid userId,
        int keepCount,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Delete password history entries beyond the keepCount, keeping the most recent ones
        await connection.ExecuteAsync(@"
            WITH RankedHistory AS (
                SELECT [Id],
                       ROW_NUMBER() OVER (ORDER BY [CreatedAt] DESC) AS RowNum
                FROM [dbo].[PasswordHistory]
                WHERE [UserId] = @UserId
            )
            DELETE FROM [dbo].[PasswordHistory]
            WHERE [Id] IN (
                SELECT [Id] FROM RankedHistory WHERE RowNum > @KeepCount
            )",
            new { UserId = userId, KeepCount = keepCount });
    }
}
