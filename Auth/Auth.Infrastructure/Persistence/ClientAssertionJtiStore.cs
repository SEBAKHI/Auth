using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of <see cref="IClientAssertionJtiStore"/> (OI-128).
/// </summary>
/// <remarks>
/// The same shape as <see cref="SingleUseCodeStatements"/>: single use is a condition of the very
/// statement that records the use, never a read followed by a write. A read first lets every
/// request that read before the first write through, so one captured assertion replayed in a burst
/// would buy several tokens.
/// </remarks>
public class ClientAssertionJtiStore : IClientAssertionJtiStore
{
    /// <summary>
    /// Records the jti unless it is recorded already. The existence test runs under an update lock
    /// held to the end of the statement (HOLDLOCK makes it a key-range lock even when the row is
    /// absent), so of two concurrent uses the second waits, then sees the first one's row and
    /// inserts nothing. Parameters: <c>@ApplicationId</c>, <c>@Jti</c>, <c>@ExpiresAt</c>.
    /// </summary>
    internal const string ConsumeOnce = @"
        INSERT INTO [dbo].[ClientAssertionJtis] ([ApplicationId], [Jti], [ExpiresAt])
        SELECT @ApplicationId, @Jti, @ExpiresAt
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[ClientAssertionJtis] WITH (UPDLOCK, HOLDLOCK)
            WHERE [ApplicationId] = @ApplicationId AND [Jti] = @Jti
        )";

    /// <summary>The column's width: a longer jti is refused before it gets here.</summary>
    public const int MaxJtiLength = 200;

    private readonly IDbConnectionFactory _connectionFactory;

    public ClientAssertionJtiStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(
        Guid applicationId, string jti, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        try
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                ConsumeOnce,
                new
                {
                    ApplicationId = applicationId,
                    Jti = new DbString { Value = jti, Length = MaxJtiLength, IsAnsi = false },
                    ExpiresAt = expiresAtUtc,
                },
                cancellationToken: cancellationToken));

            return inserted == 1;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // The primary key is the last line: whatever reached it second is a replay.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(@"
            DELETE TOP (@BatchSize) FROM [dbo].[ClientAssertionJtis]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize },
            cancellationToken: cancellationToken));
    }
}
