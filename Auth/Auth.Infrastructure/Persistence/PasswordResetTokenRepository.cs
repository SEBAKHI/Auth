using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the password reset token repository.
/// </summary>
public class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PasswordResetTokenRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<PasswordResetTokenDto>(@"
            SELECT
                [Id], [UserId], [TokenHash], [ExpiresAt], [UsedAt], [CreatedAt]
            FROM [dbo].[PasswordResetTokens]
            WHERE [TokenHash] = @TokenHash
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()",
            new { TokenHash = tokenHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(PasswordResetToken token, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PasswordResetTokens] (
                [Id], [UserId], [TokenHash], [ExpiresAt], [UsedAt], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @TokenHash, @ExpiresAt, @UsedAt, @CreatedAt
            )",
            new
            {
                token.Id,
                token.UserId,
                token.TokenHash,
                token.ExpiresAt,
                token.UsedAt,
                token.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PasswordResetTokens] SET
                [UsedAt] = GETUTCDATE()
            WHERE [Id] = @TokenId
              AND [UsedAt] IS NULL",
            new { TokenId = tokenId });
    }

    /// <inheritdoc />
    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PasswordResetTokens] SET
                [UsedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<bool> HasLiveTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Seeks IX_PasswordResetTokens_UserId; EXISTS stops at the first row.
        return await connection.ExecuteScalarAsync<bool>(@"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[PasswordResetTokens]
                WHERE [UserId] = @UserId
                  AND [UsedAt] IS NULL
                  AND [ExpiresAt] > GETUTCDATE()) THEN 1 ELSE 0 END AS BIT)",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[PasswordResetTokens]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    // Internal DTO for mapping from database
    private record PasswordResetTokenDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public DateTime CreatedAt { get; init; }

        public PasswordResetToken ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            ExpiresAt,
            UsedAt,
            CreatedAt);
    }
}
