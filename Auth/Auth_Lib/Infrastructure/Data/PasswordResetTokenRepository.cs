using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth_Lib.Infrastructure.Data;

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
    public async Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
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
    public async Task CreateAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
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
    public async Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken = default)
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
    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
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
    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Delete tokens that are expired and older than 7 days
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[PasswordResetTokens]
            WHERE [ExpiresAt] < DATEADD(DAY, -7, GETUTCDATE())");
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
