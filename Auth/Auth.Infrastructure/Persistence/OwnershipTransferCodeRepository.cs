using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the organization ownership transfer code repository.
/// </summary>
public class OwnershipTransferCodeRepository : IOwnershipTransferCodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OwnershipTransferCodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<OwnershipTransferCode?> GetValidForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OwnershipTransferCodeDto>(@"
            SELECT TOP 1
                [Id], [OrganizationId], [TargetUserId], [InitiatedBy], [CodeHash], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            FROM [dbo].[OwnershipTransferCodes]
            WHERE [OrganizationId] = @OrganizationId
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > GETUTCDATE()
            ORDER BY [CreatedAt] DESC",
            new { OrganizationId = organizationId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(OwnershipTransferCode code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OwnershipTransferCodes] (
                [Id], [OrganizationId], [TargetUserId], [InitiatedBy], [CodeHash], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt]
            ) VALUES (
                @Id, @OrganizationId, @TargetUserId, @InitiatedBy, @CodeHash, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt
            )",
            new
            {
                code.Id,
                code.OrganizationId,
                code.TargetUserId,
                code.InitiatedBy,
                code.CodeHash,
                code.ExpiresAt,
                code.UsedAt,
                code.AttemptCount,
                code.CreatedAt
            });
    }

    /// <inheritdoc />
    public async Task MarkAsUsedAsync(Guid codeId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OwnershipTransferCodes] SET
                [UsedAt] = GETUTCDATE()
            WHERE [Id] = @CodeId
              AND [UsedAt] IS NULL",
            new { CodeId = codeId });
    }

    /// <inheritdoc />
    public async Task IncrementAttemptCountAsync(Guid codeId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OwnershipTransferCodes] SET
                [AttemptCount] = [AttemptCount] + 1
            WHERE [Id] = @CodeId",
            new { CodeId = codeId });
    }

    /// <inheritdoc />
    public async Task InvalidateAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OwnershipTransferCodes] SET
                [UsedAt] = GETUTCDATE()
            WHERE [OrganizationId] = @OrganizationId
              AND [UsedAt] IS NULL",
            new { OrganizationId = organizationId });
    }

    /// <inheritdoc />
    public async Task<int> GetRecentCountForOrganizationAsync(Guid organizationId, TimeSpan window, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OwnershipTransferCodes]
            WHERE [OrganizationId] = @OrganizationId
              AND [CreatedAt] > DATEADD(SECOND, -@WindowSeconds, GETUTCDATE())",
            new { OrganizationId = organizationId, WindowSeconds = (int)window.TotalSeconds });

        return count;
    }

    // Internal DTO for mapping from database
    private record OwnershipTransferCodeDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid TargetUserId { get; init; }
        public Guid InitiatedBy { get; init; }
        public string CodeHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public DateTime CreatedAt { get; init; }

        public OwnershipTransferCode ToEntity() => new(
            Id,
            OrganizationId,
            TargetUserId,
            InitiatedBy,
            CodeHash,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            CreatedAt);
    }
}
