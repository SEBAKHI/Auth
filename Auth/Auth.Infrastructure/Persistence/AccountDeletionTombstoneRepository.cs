using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the destruction registry. Rows live for the
/// configured reservation window and are then swept: a keyed digest of an
/// e-mail address is pseudonymised personal data, so it gets an end of life
/// like every other record.
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
                INSERT ([Id], [EmailHash], [DeletedAtUtc], [PolicyVersion], [KeyVersion])
                VALUES (@Id, @EmailHash, @DeletedAtUtc, @PolicyVersion, @KeyVersion);",
            new
            {
                tombstone.Id,
                tombstone.EmailHash,
                tombstone.DeletedAtUtc,
                tombstone.PolicyVersion,
                tombstone.KeyVersion
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
    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            "DELETE FROM [dbo].[AccountDeletionTombstones] WHERE [DeletedAtUtc] < @CutoffUtc",
            new { CutoffUtc = cutoffUtc });
    }
}
