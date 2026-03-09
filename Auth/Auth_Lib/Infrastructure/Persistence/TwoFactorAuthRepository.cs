using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth_Lib.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the two-factor authentication repository.
/// </summary>
public class TwoFactorAuthRepository : ITwoFactorAuthRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TwoFactorAuthRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TwoFactorAuth?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorAuthDto>(@"
            SELECT
                [Id], [UserId], [SecretKey], [RecoveryCodes],
                [IsEnabled], [EnabledAt], [LastUsedAt],
                [FailedAttempts], [LockedUntil],
                [CreatedAt], [ModifiedAt]
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId",
            new { UserId = userId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(TwoFactorAuth twoFactorAuth, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[TwoFactorAuth] (
                [Id], [UserId], [SecretKey], [RecoveryCodes],
                [IsEnabled], [EnabledAt], [LastUsedAt],
                [FailedAttempts], [LockedUntil],
                [CreatedAt], [ModifiedAt]
            ) VALUES (
                @Id, @UserId, @SecretKey, @RecoveryCodes,
                @IsEnabled, @EnabledAt, @LastUsedAt,
                @FailedAttempts, @LockedUntil,
                @CreatedAt, @ModifiedAt
            )",
            new
            {
                twoFactorAuth.Id,
                twoFactorAuth.UserId,
                twoFactorAuth.SecretKey,
                twoFactorAuth.RecoveryCodes,
                twoFactorAuth.IsEnabled,
                twoFactorAuth.EnabledAt,
                twoFactorAuth.LastUsedAt,
                twoFactorAuth.FailedAttempts,
                twoFactorAuth.LockedUntil,
                twoFactorAuth.CreatedAt,
                twoFactorAuth.ModifiedAt
            });
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TwoFactorAuth twoFactorAuth, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[TwoFactorAuth] SET
                [SecretKey] = @SecretKey,
                [RecoveryCodes] = @RecoveryCodes,
                [IsEnabled] = @IsEnabled,
                [EnabledAt] = @EnabledAt,
                [LastUsedAt] = @LastUsedAt,
                [FailedAttempts] = @FailedAttempts,
                [LockedUntil] = @LockedUntil,
                [ModifiedAt] = @ModifiedAt
            WHERE [Id] = @Id",
            new
            {
                twoFactorAuth.Id,
                twoFactorAuth.SecretKey,
                twoFactorAuth.RecoveryCodes,
                twoFactorAuth.IsEnabled,
                twoFactorAuth.EnabledAt,
                twoFactorAuth.LastUsedAt,
                twoFactorAuth.FailedAttempts,
                twoFactorAuth.LockedUntil,
                twoFactorAuth.ModifiedAt
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId",
            new { UserId = userId });
    }

    // Internal DTO for mapping from database
    private record TwoFactorAuthDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string SecretKey { get; init; } = string.Empty;
        public string? RecoveryCodes { get; init; }
        public bool IsEnabled { get; init; }
        public DateTime? EnabledAt { get; init; }
        public DateTime? LastUsedAt { get; init; }
        public int FailedAttempts { get; init; }
        public DateTime? LockedUntil { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ModifiedAt { get; init; }

        public TwoFactorAuth ToEntity() => new(
            Id,
            UserId,
            SecretKey,
            RecoveryCodes,
            IsEnabled,
            EnabledAt,
            LastUsedAt,
            FailedAttempts,
            LockedUntil,
            CreatedAt,
            ModifiedAt);
    }
}
