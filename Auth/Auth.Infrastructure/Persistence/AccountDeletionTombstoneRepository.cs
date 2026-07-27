using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the zero-PII destruction registry. Rows are
/// permanent: there is deliberately no delete method.
/// </summary>
public class AccountDeletionTombstoneRepository : IAccountDeletionTombstoneRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AccountDeletionTombstoneRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(AccountDeletionTombstone tombstone, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            MERGE [dbo].[AccountDeletionTombstones] WITH (HOLDLOCK) AS [target]
            USING (SELECT @EmailHash AS [EmailHash]) AS [source]
            ON [target].[EmailHash] = [source].[EmailHash]
            WHEN NOT MATCHED THEN
                INSERT ([Id], [EmailHash], [UsernameHash], [DeletedAtUtc], [PolicyVersion])
                VALUES (@Id, @EmailHash, @UsernameHash, @DeletedAtUtc, @PolicyVersion);",
            new
            {
                tombstone.Id,
                tombstone.EmailHash,
                tombstone.UsernameHash,
                tombstone.DeletedAtUtc,
                tombstone.PolicyVersion
            });
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByEmailHashAsync(string emailHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM [dbo].[AccountDeletionTombstones] WHERE [EmailHash] = @EmailHash",
            new { EmailHash = emailHash });
        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByUsernameHashAsync(string usernameHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM [dbo].[AccountDeletionTombstones] WHERE [UsernameHash] = @UsernameHash",
            new { UsernameHash = usernameHash });
        return count > 0;
    }
}
