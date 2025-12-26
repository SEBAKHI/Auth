using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth_Lib.Infrastructure.Data;

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
    public async Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<RefreshTokenDto>(
            "SELECT * FROM [dbo].[RefreshTokens] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
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
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "EXEC [dbo].[sp_CreateRefreshToken] @UserId, @Token, @TokenHash, @JwtId, @ApplicationId, @DeviceInfo, @IpAddress, @ExpiresAt",
            new
            {
                token.UserId,
                token.Token,
                token.TokenHash,
                token.JwtId,
                token.ApplicationId,
                token.DeviceInfo,
                token.IpAddress,
                token.ExpiresAt
            });

        return token;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[RefreshTokens] SET
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [ReasonRevoked] = @ReasonRevoked,
                [ReplacedByToken] = @ReplacedByToken
            WHERE [Id] = @Id",
            new
            {
                token.Id,
                token.RevokedAt,
                token.RevokedBy,
                token.ReasonRevoked,
                token.ReplacedByToken
            });
    }

    /// <inheritdoc />
    public async Task RevokeAllForUserAsync(
        Guid userId,
        Guid? revokedBy,
        string reason,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
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
        CancellationToken cancellationToken = default)
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
    public async Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
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
    public async Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[RefreshTokens]
            WHERE ([ExpiresAt] < @OlderThan OR [RevokedAt] < @OlderThan)",
            new { OlderThan = olderThan });
    }

    private record RefreshTokenDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string Token { get; init; } = string.Empty;
        public string TokenHash { get; init; } = string.Empty;
        public string JwtId { get; init; } = string.Empty;
        public Guid? ApplicationId { get; init; }
        public string? DeviceInfo { get; init; }
        public string? IpAddress { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? ReplacedByToken { get; init; }
        public string? ReasonRevoked { get; init; }

        public RefreshToken ToEntity() => new(
            Id,
            UserId,
            Token,
            TokenHash,
            JwtId,
            ApplicationId,
            DeviceInfo,
            IpAddress,
            CreatedAt,
            ExpiresAt,
            RevokedAt,
            RevokedBy,
            ReplacedByToken,
            ReasonRevoked);
    }
}
