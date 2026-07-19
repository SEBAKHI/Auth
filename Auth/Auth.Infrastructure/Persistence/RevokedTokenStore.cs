using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the durable revoked-token store.
/// </summary>
public class RevokedTokenStore : IRevokedTokenStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RevokedTokenStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(TokenRevocation revocation, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[RevokedTokens] (
                [RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt]
            ) VALUES (
                @RevocationType, @RevocationKey, @EffectiveAt, @ExpiresAt
            )",
            new
            {
                RevocationType = (byte)revocation.Type,
                RevocationKey = revocation.Key,
                revocation.EffectiveAt,
                revocation.ExpiresAt
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TokenRevocation>> GetActiveAsync(DateTime now, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(byte RevocationType, string RevocationKey, DateTime EffectiveAt, DateTime ExpiresAt)>(@"
            SELECT [RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt]
            FROM [dbo].[RevokedTokens]
            WHERE [ExpiresAt] > @Now",
            new { Now = now });

        return rows
            .Select(r => new TokenRevocation((RevocationType)r.RevocationType, r.RevocationKey, r.EffectiveAt, r.ExpiresAt))
            .ToList();
    }

    /// <inheritdoc />
    public async Task PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[RevokedTokens]
            WHERE [ExpiresAt] <= @OlderThan",
            new { OlderThan = olderThan });
    }
}
