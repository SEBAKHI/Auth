using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Secrets;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the rotation-impact reader. One batch, one @Now, so
/// every figure the administrator sees describes the same instant.
/// </summary>
public class SecretRotationImpactRepository : ISecretRotationImpactRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SecretRotationImpactRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<SecretRotationImpactSnapshot> GetImpactAsync(
        TimeSpan accessTokenLifetime,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Every user count joins Users and excludes soft-deleted accounts: stale
        // credential rows belonging to deleted people would inflate a number the
        // administrator is about to make a decision on.
        using var grid = await connection.QueryMultipleAsync(@"
            DECLARE @Now DATETIME2 = GETUTCDATE();

            -- Access tokens are stateless, so the only trace of a live one is
            -- the refresh token minted beside it. Anything older than the access
            -- token lifetime plus clock skew has already been replaced.
            SELECT COUNT(DISTINCT t.[UserId])
            FROM [dbo].[RefreshTokens] t
            INNER JOIN [dbo].[Users] u ON u.[Id] = t.[UserId] AND u.[IsDeleted] = 0
            WHERE t.[CreatedAt] > DATEADD(SECOND, -@AccessTokenLifetimeSeconds, @Now);

            SELECT COUNT(DISTINCT s.[UserId])
            FROM [dbo].[UserSessions] s
            INNER JOIN [dbo].[Users] u ON u.[Id] = s.[UserId] AND u.[IsDeleted] = 0
            WHERE s.[EndedAt] IS NULL AND s.[ExpiresAt] > @Now;

            SELECT COUNT(DISTINCT t.[UserId])
            FROM [dbo].[RefreshTokens] t
            INNER JOIN [dbo].[Users] u ON u.[Id] = t.[UserId] AND u.[IsDeleted] = 0
            WHERE t.[RevokedAt] IS NULL AND t.[ExpiresAt] > @Now;

            SELECT COUNT(DISTINCT i.[UserId])
            FROM [dbo].[IdpSessions] i
            INNER JOIN [dbo].[Users] u ON u.[Id] = i.[UserId] AND u.[IsDeleted] = 0
            WHERE i.[RevokedAt] IS NULL AND i.[ExpiresAt] > @Now;

            SELECT COUNT(*)
            FROM [dbo].[PasswordResetTokens]
            WHERE [UsedAt] IS NULL AND [ExpiresAt] > @Now;

            SELECT COUNT(*)
            FROM [dbo].[TwoFactorChallenges]
            WHERE [UsedAt] IS NULL AND [ExpiresAt] > @Now;

            SELECT COUNT(*)
            FROM [dbo].[WebhookKeys]
            WHERE [RevokedAt] IS NULL AND ([ExpiresAt] IS NULL OR [ExpiresAt] > @Now);",
            new { AccessTokenLifetimeSeconds = (int)accessTokenLifetime.TotalSeconds });

        return new SecretRotationImpactSnapshot
        {
            UsersWithLiveAccessTokens = await grid.ReadFirstAsync<int>(),
            UsersWithActiveSessions = await grid.ReadFirstAsync<int>(),
            UsersWithActiveRefreshTokens = await grid.ReadFirstAsync<int>(),
            UsersWithActiveIdpSessions = await grid.ReadFirstAsync<int>(),
            PendingPasswordResets = await grid.ReadFirstAsync<int>(),
            PendingTwoFactorChallenges = await grid.ReadFirstAsync<int>(),
            ActiveWebhookKeys = await grid.ReadFirstAsync<int>()
        };
    }
}
