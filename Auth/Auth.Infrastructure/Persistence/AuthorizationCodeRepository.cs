using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the OAuth authorization code repository.
/// </summary>
public class AuthorizationCodeRepository : IAuthorizationCodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AuthorizationCodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode> CreateAsync(AuthorizationCode code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[AuthorizationCodes] (
                [Id], [ApplicationId], [UserId], [CodeHash], [RedirectUri],
                [CodeChallenge], [ExpiresAt], [ConsumedAt], [CreatedAt], [IpAddress]
            ) VALUES (
                @Id, @ApplicationId, @UserId, @CodeHash, @RedirectUri,
                @CodeChallenge, @ExpiresAt, @ConsumedAt, @CreatedAt, @IpAddress
            )",
            new
            {
                code.Id,
                code.ApplicationId,
                code.UserId,
                code.CodeHash,
                code.RedirectUri,
                code.CodeChallenge,
                code.ExpiresAt,
                code.ConsumedAt,
                code.CreatedAt,
                code.IpAddress
            });

        return code;
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode?> ConsumeByCodeHashAsync(string codeHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Single atomic statement: only the caller that flips ConsumedAt gets
        // the row back, so a code can never be redeemed twice even under
        // concurrent requests.
        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationCodeDto>(@"
            UPDATE [dbo].[AuthorizationCodes] SET
                [ConsumedAt] = GETUTCDATE()
            OUTPUT
                INSERTED.[Id], INSERTED.[ApplicationId], INSERTED.[UserId],
                INSERTED.[CodeHash], INSERTED.[RedirectUri], INSERTED.[CodeChallenge],
                INSERTED.[ExpiresAt], INSERTED.[ConsumedAt], INSERTED.[CreatedAt],
                INSERTED.[IpAddress], INSERTED.[IssuedSessionId]
            WHERE [CodeHash] = @CodeHash
              AND [ConsumedAt] IS NULL",
            new { CodeHash = codeHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<AuthorizationCode?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<AuthorizationCodeDto>(@"
            SELECT
                [Id], [ApplicationId], [UserId], [CodeHash], [RedirectUri],
                [CodeChallenge], [ExpiresAt], [ConsumedAt], [CreatedAt], [IpAddress],
                [IssuedSessionId]
            FROM [dbo].[AuthorizationCodes]
            WHERE [CodeHash] = @CodeHash",
            new { CodeHash = codeHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[AuthorizationCodes]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    /// <inheritdoc />
    public async Task RecordIssuedSessionAsync(
        Guid codeId, Guid sessionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Written after the exchange succeeded, not during it: until tokens
        // actually exist there is nothing a replay would need to revoke, and a
        // failure here must never undo a sign-in that already completed.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[AuthorizationCodes] SET
                [IssuedSessionId] = @SessionId
            WHERE [Id] = @CodeId",
            new { CodeId = codeId, SessionId = sessionId });
    }

    // Internal DTO for mapping from database
    private record AuthorizationCodeDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid UserId { get; init; }
        public string CodeHash { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string CodeChallenge { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? ConsumedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? IpAddress { get; init; }
        public Guid? IssuedSessionId { get; init; }

        public AuthorizationCode ToEntity() => new(
            Id,
            ApplicationId,
            UserId,
            CodeHash,
            RedirectUri,
            CodeChallenge,
            CreatedAt,
            ExpiresAt,
            ConsumedAt,
            IpAddress,
            IssuedSessionId);
    }
}
