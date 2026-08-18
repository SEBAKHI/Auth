using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the refresh token repository.
/// </summary>
public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RefreshTokenRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<RefreshTokenDto>(
            "SELECT * FROM [dbo].[RefreshTokens] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Use inline query instead of stored procedure to avoid column mapping issues
        var dto = await connection.QueryFirstOrDefaultAsync<RefreshTokenDto>(@"
            SELECT * FROM [dbo].[RefreshTokens]
            WHERE [TokenHash] = @TokenHash",
            new { TokenHash = tokenHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "EXEC [dbo].[sp_CreateRefreshToken] @UserId, @TokenHash, @JwtId, @ApplicationId, @DeviceInfo, @IpAddress, @ExpiresAt, @SessionId",
            new
            {
                token.UserId,
                token.TokenHash,
                token.JwtId,
                token.ApplicationId,
                token.DeviceInfo,
                token.IpAddress,
                token.ExpiresAt,
                token.SessionId
            });

        return token;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked,
                [ReplacedByTokenHash] = @ReplacedByTokenHash
            WHERE [Id] = @Id",
            new
            {
                token.Id,
                token.RevokedAt,
                token.RevokedBy,
                token.ReasonRevoked,
                token.ReplacedByTokenHash
            });
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            "EXEC [dbo].[sp_RevokeAllUserTokens] @UserId, @RevokedBy, @ReasonRevoked",
            new
            {
                UserId = userId,
                RevokedBy = revokedBy,
                ReasonRevoked = reason
            });
    }

    /// <inheritdoc />
    public async Task RevokeByDeviceAsync(
        Guid userId,
        string deviceInfo,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked
            WHERE [UserId] = @UserId
              AND [DeviceInfo] = @DeviceInfo
              AND [RevokedAt] IS NULL",
            new
            {
                UserId = userId,
                DeviceInfo = deviceInfo,
                RevokedBy = revokedBy,
                ReasonRevoked = reason
            });
    }

    /// <inheritdoc />
    public async Task RevokeBySessionIdAsync(
        Guid sessionId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked
            WHERE [SessionId] = @SessionId
              AND [RevokedAt] IS NULL",
            new
            {
                SessionId = sessionId,
                RevokedBy = revokedBy,
                ReasonRevoked = reason
            });
    }

    /// <inheritdoc />
    public async Task RevokeAllForApplicationAsync(
        Guid applicationId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Scoped by ApplicationId, so platform tokens (null) and tokens for
        // other applications survive: switching one application off must not
        // sign anyone out of the rest.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked
            WHERE [ApplicationId] = @ApplicationId
              AND [RevokedAt] IS NULL",
            new
            {
                ApplicationId = applicationId,
                RevokedBy = revokedBy,
                ReasonRevoked = reason
            });
    }

    /// <inheritdoc />
    public async Task RevokeForUserAndApplicationAsync(
        Guid userId,
        Guid applicationId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked
            WHERE [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [RevokedAt] IS NULL",
            new
            {
                UserId = userId,
                ApplicationId = applicationId,
                RevokedBy = revokedBy,
                ReasonRevoked = reason
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<RefreshTokenDto>(@"
            SELECT * FROM [dbo].[RefreshTokens]
            WHERE [UserId] = @UserId
              AND [RevokedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
            ORDER BY [CreatedAt] DESC",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[RefreshTokens]
            WHERE [RevokedAt] IS NOT NULL AND [RevokedAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        deleted += await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[RefreshTokens]
            WHERE [RevokedAt] IS NULL AND [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    private record RefreshTokenDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public string JwtId { get; init; } = string.Empty;
        public Guid? SessionId { get; init; }
        public Guid? ApplicationId { get; init; }
        public string? DeviceInfo { get; init; }
        public string? IpAddress { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? ReplacedByTokenHash { get; init; }
        public string? ReasonRevoked { get; init; }

        public RefreshToken ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            JwtId,
            SessionId,
            ApplicationId,
            DeviceInfo,
            IpAddress,
            CreatedAt,
            ExpiresAt,
            RevokedAt,
            RevokedBy,
            ReplacedByTokenHash,
            ReasonRevoked);
    }
}
