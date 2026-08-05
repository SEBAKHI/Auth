using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the deletion re-authentication OTP repository.
/// Mirrors the EmailVerificationTokens data access it is modeled on.
/// </summary>
public class AccountDeletionVerificationRepository : IAccountDeletionVerificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AccountDeletionVerificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task CreateAsync(AccountDeletionVerification verification, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[AccountDeletionVerifications] (
                [Id], [UserId], [Email], [OtpHash], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            ) VALUES (
                @Id, @UserId, @Email, @OtpHash, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt
            )",
            new
            {
                verification.Id,
                verification.UserId,
                Email = verification.Email.Value,
                verification.OtpHash,
                verification.ExpiresAt,
                verification.UsedAt,
                verification.AttemptCount,
                verification.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDeletionVerification>> GetValidForEmailAsync(
        string email, int maxCandidates, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<VerificationDto>(@"
            SELECT TOP (@MaxCandidates)
                   [Id], [UserId], [Email], [OtpHash], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            FROM [dbo].[AccountDeletionVerifications]
            WHERE [Email] = @Email AND [UsedAt] IS NULL AND [ExpiresAt] > GETUTCDATE()
            ORDER BY [CreatedAt] DESC",
            new { Email = email.ToLowerInvariant(), MaxCandidates = maxCandidates });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task MarkAsUsedAsync(Guid verificationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "UPDATE [dbo].[AccountDeletionVerifications] SET [UsedAt] = GETUTCDATE() WHERE [Id] = @Id",
            new { Id = verificationId });
    }

    /// <inheritdoc />
    public async Task IncrementAttemptCountAsync(Guid verificationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "UPDATE [dbo].[AccountDeletionVerifications] SET [AttemptCount] = [AttemptCount] + 1 WHERE [Id] = @Id",
            new { Id = verificationId });
    }

    /// <inheritdoc />
    public async Task<int> GetRecentCountAsync(string email, TimeSpan window, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[AccountDeletionVerifications]
            WHERE [Email] = @Email AND [CreatedAt] > @Since",
            new { Email = email.ToLowerInvariant(), Since = DateTime.UtcNow - window });
    }

    /// <inheritdoc />
    public async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[AccountDeletionVerifications] WHERE [ExpiresAt] < GETUTCDATE() OR [UsedAt] IS NOT NULL");
    }

    // Internal DTO for mapping from database (Email is a value object on the entity)
    private record VerificationDto
    {
        public Guid Id { get; init; }
        public Guid? UserId { get; init; }
        public string Email { get; init; } = string.Empty;
        public string OtpHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public DateTime CreatedAt { get; init; }

        public AccountDeletionVerification ToEntity() => new(
            Id, UserId, Email, OtpHash, ExpiresAt, UsedAt, AttemptCount, CreatedAt);
    }
}
