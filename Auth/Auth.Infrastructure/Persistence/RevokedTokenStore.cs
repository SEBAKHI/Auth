using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the durable revoked-token store.
/// </summary>
/// <remarks>
/// A user-level row counts until <c>max(ExpiresAt, EffectiveAt + RevocationHorizon.User)</c>,
/// whatever expiry it was stored with: rows written before X04 carry one hour. Both statements
/// below say so, and keep the seekable <c>[ExpiresAt] &gt; @Cut</c> first: a row's EffectiveAt is
/// never after its ExpiresAt, so every row the horizon keeps also passes it.
/// </remarks>
public class RevokedTokenStore : IRevokedTokenStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RevokedTokenStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    internal const string InsertSql = @"
            INSERT INTO [dbo].[RevokedTokens] (
                [RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt]
            ) VALUES (
                @RevocationType, @RevocationKey, @EffectiveAt, @ExpiresAt
            )";

    internal const string GetActiveSql = @"
            SELECT [RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt]
            FROM [dbo].[RevokedTokens]
            WHERE [ExpiresAt] > @Cut
              AND ([ExpiresAt] > @Now OR ([RevocationType] = @UserRevocation AND [EffectiveAt] > @Cut))";

    internal const string PurgeSql = @"
            DELETE FROM [dbo].[RevokedTokens]
            WHERE [ExpiresAt] <= @OlderThan
              AND NOT ([RevocationType] = @UserRevocation AND [EffectiveAt] > @Cut)";

    /// <inheritdoc />
    public async Task AddAsync(TokenRevocation revocation, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                RevocationType = (byte)revocation.Type,
                RevocationKey = revocation.Key,
                revocation.EffectiveAt,
                revocation.ExpiresAt
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TokenRevocation>> GetActiveAsync(DateTime now, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(byte RevocationType, string RevocationKey, DateTime EffectiveAt, DateTime ExpiresAt)>(
            new CommandDefinition(
                GetActiveSql,
                new { Now = now, Cut = now - RevocationHorizon.User, UserRevocation = (byte)RevocationType.User },
                cancellationToken: cancellationToken));

        return rows
            .Select(r => new TokenRevocation((RevocationType)r.RevocationType, r.RevocationKey, r.EffectiveAt, r.ExpiresAt))
            .ToList();
    }

    /// <inheritdoc />
    public async Task PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            PurgeSql,
            new
            {
                OlderThan = olderThan,
                Cut = olderThan - RevocationHorizon.User,
                UserRevocation = (byte)RevocationType.User
            },
            cancellationToken: cancellationToken));
    }
}
