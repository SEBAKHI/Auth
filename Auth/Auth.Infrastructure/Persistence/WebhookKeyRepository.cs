using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the webhook key repository.
/// </summary>
public class WebhookKeyRepository : IWebhookKeyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WebhookKeyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<WebhookKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<WebhookKeyDto>(
            "SELECT * FROM [dbo].[WebhookKeys] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<WebhookKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<WebhookKeyDto>(@"
            SELECT * FROM [dbo].[WebhookKeys]
            WHERE [KeyHash] = @KeyHash
              AND [RevokedAt] IS NULL
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { KeyHash = keyHash });

        return dto?.ToEntity();
    }

    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.WebhookKeys.Name, ["[Name]"]),
        (SortFields.WebhookKeys.CreatedAt, ["[CreatedAt]"]),
        (SortFields.WebhookKeys.ExpiresAt, ["[ExpiresAt]"]),
        (SortFields.WebhookKeys.RevokedAt, ["[RevokedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookKey>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[CreatedAt] DESC", "[Id]");
        var dtos = await connection.QueryAsync<WebhookKeyDto>($@"
            SELECT * FROM [dbo].[WebhookKeys]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY {orderBy}",
            new { ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<WebhookKey> CreateAsync(WebhookKey webhookKey, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[WebhookKeys] (
                [Id], [ApplicationId], [Name], [Description], [KeyPrefix], [KeyHash],
                [TargetUrl], [Environment], [CreatedAt], [CreatedBy], [ExpiresAt]
            ) VALUES (
                @Id, @ApplicationId, @Name, @Description, @KeyPrefix, @KeyHash,
                @TargetUrl, @Environment, @CreatedAt, @CreatedBy, @ExpiresAt
            )",
            new
            {
                webhookKey.Id,
                webhookKey.ApplicationId,
                webhookKey.Name,
                webhookKey.Description,
                webhookKey.KeyPrefix,
                webhookKey.KeyHash,
                webhookKey.TargetUrl,
                webhookKey.Environment,
                webhookKey.CreatedAt,
                webhookKey.CreatedBy,
                webhookKey.ExpiresAt
            });

        return webhookKey;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(WebhookKey webhookKey, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[WebhookKeys] SET
                [ExpiresAt] = @ExpiresAt,
                [LastUsedAt] = @LastUsedAt,
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [RevokeReason] = @RevokeReason
            WHERE [Id] = @Id",
            new
            {
                webhookKey.Id,
                webhookKey.ExpiresAt,
                webhookKey.LastUsedAt,
                webhookKey.RevokedAt,
                webhookKey.RevokedBy,
                webhookKey.RevokeReason
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[WebhookKeys] WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(Guid webhookKeyId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[WebhookKeys] SET [LastUsedAt] = GETUTCDATE()
            WHERE [Id] = @Id",
            new { Id = webhookKeyId });
    }

    private record WebhookKeyDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string KeyPrefix { get; init; } = string.Empty;
        public string KeyHash { get; init; } = string.Empty;
        public string TargetUrl { get; init; } = string.Empty;
        public string Environment { get; init; } = "production";
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime? LastUsedAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? RevokeReason { get; init; }

        public WebhookKey ToEntity() => new(
            Id,
            ApplicationId,
            Name,
            Description,
            KeyPrefix,
            KeyHash,
            TargetUrl,
            Environment,
            CreatedAt,
            CreatedBy,
            ExpiresAt,
            LastUsedAt,
            RevokedAt,
            RevokedBy,
            RevokeReason);
    }
}
