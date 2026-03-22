using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the email verification token repository.
/// </summary>
public class EmailVerificationTokenRepository : IEmailVerificationTokenRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public EmailVerificationTokenRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<EmailVerificationToken?> GetValidTokenForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<EmailVerificationTokenDto>(@"
            SELECT TOP 1
                [Id], [UserId], [OtpHash], [Email], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            FROM [dbo].[EmailVerificationTokens]
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
            ORDER BY [CreatedAt] DESC",
            new { UserId = userId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(EmailVerificationToken token, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[EmailVerificationTokens] (
                [Id], [UserId], [OtpHash], [Email], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @OtpHash, @Email, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt
            )",
            new
            {
                token.Id,
                token.UserId,
                token.OtpHash,
                Email = token.Email.Value,
                token.ExpiresAt,
                token.UsedAt,
                token.AttemptCount,
                token.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[EmailVerificationTokens] SET
                [UsedAt] = GETUTCDATE()
            WHERE [Id] = @TokenId
              AND [UsedAt] IS NULL",
            new { TokenId = tokenId });
    }

    /// <inheritdoc />
    public async Task IncrementAttemptCountAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[EmailVerificationTokens] SET
                [AttemptCount] = [AttemptCount] + 1
            WHERE [Id] = @TokenId",
            new { TokenId = tokenId });
    }

    /// <inheritdoc />
    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[EmailVerificationTokens] SET
                [UsedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<int> GetRecentTokenCountAsync(string email, TimeSpan window, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[EmailVerificationTokens]
            WHERE [Email] = @Email
              AND [CreatedAt] > DATEADD(SECOND, -@WindowSeconds, GETUTCDATE())",
            new { Email = email.ToLowerInvariant(), WindowSeconds = (int)window.TotalSeconds });

        return count;
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Delete tokens that are expired and older than 7 days
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[EmailVerificationTokens]
            WHERE [ExpiresAt] < DATEADD(DAY, -7, GETUTCDATE())");
    }

    // Internal DTO for mapping from database
    private record EmailVerificationTokenDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string OtpHash { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public DateTime CreatedAt { get; init; }

        public EmailVerificationToken ToEntity() => new(
            Id,
            UserId,
            OtpHash,
            Email,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            CreatedAt);
    }
}
