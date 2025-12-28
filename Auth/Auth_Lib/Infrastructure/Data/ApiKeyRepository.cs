using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth_Lib.Infrastructure.Data;

/// <summary>
/// Dapper implementation of the API key repository.
/// </summary>
public class ApiKeyRepository : IApiKeyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ApiKeyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApiKeyDto>(
            "SELECT * FROM [dbo].[ApiKeys] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApiKeyDto>(@"
            SELECT * FROM [dbo].[ApiKeys]
            WHERE [KeyHash] = @KeyHash AND [RevokedAt] IS NULL",
            new { KeyHash = keyHash });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<ApiKeyDto>(@"
            SELECT * FROM [dbo].[ApiKeys]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY [CreatedAt] DESC",
            new { ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<ApiKey> CreateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[ApiKeys] (
                [Id], [ApplicationId], [Name], [Description], [KeyPrefix], [KeyHash],
                [Environment], [RateLimitPerMinute], [RateLimitPerDay],
                [AllowedIps], [AllowedOrigins], [CreatedAt], [CreatedBy], [ExpiresAt]
            ) VALUES (
                @Id, @ApplicationId, @Name, @Description, @KeyPrefix, @KeyHash,
                @Environment, @RateLimitPerMinute, @RateLimitPerDay,
                @AllowedIps, @AllowedOrigins, @CreatedAt, @CreatedBy, @ExpiresAt
            )",
            new
            {
                apiKey.Id,
                apiKey.ApplicationId,
                apiKey.Name,
                apiKey.Description,
                apiKey.KeyPrefix,
                apiKey.KeyHash,
                apiKey.Environment,
                apiKey.RateLimitPerMinute,
                apiKey.RateLimitPerDay,
                apiKey.AllowedIps,
                apiKey.AllowedOrigins,
                apiKey.CreatedAt,
                apiKey.CreatedBy,
                apiKey.ExpiresAt
            });

        return apiKey;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApiKeys] SET
                [LastUsedAt] = @LastUsedAt,
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [RevokeReason] = @RevokeReason
            WHERE [Id] = @Id",
            new
            {
                apiKey.Id,
                apiKey.LastUsedAt,
                apiKey.RevokedAt,
                apiKey.RevokedBy,
                apiKey.RevokeReason
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Delete scopes first
        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[ApiKeyScopes] WHERE [ApiKeyId] = @Id",
            new { Id = id });

        // Then delete the key
        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[ApiKeys] WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task AddScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[ApiKeyScopes] (
                [Id], [ApiKeyId], [PermissionId], [GrantedAt], [GrantedBy]
            ) VALUES (
                @Id, @ApiKeyId, @PermissionId, @GrantedAt, @GrantedBy
            )",
            new
            {
                scope.Id,
                scope.ApiKeyId,
                scope.PermissionId,
                scope.GrantedAt,
                scope.GrantedBy
            });
    }

    /// <inheritdoc />
    public async Task RemoveScopeAsync(Guid apiKeyId, Guid permissionId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[ApiKeyScopes]
            WHERE [ApiKeyId] = @ApiKeyId AND [PermissionId] = @PermissionId",
            new { ApiKeyId = apiKeyId, PermissionId = permissionId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetScopesAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var permissions = await connection.QueryAsync<string>(@"
            SELECT p.[Code]
            FROM [dbo].[ApiKeyScopes] aks
            INNER JOIN [dbo].[Permissions] p ON aks.[PermissionId] = p.[Id]
            WHERE aks.[ApiKeyId] = @ApiKeyId AND p.[IsActive] = 1",
            new { ApiKeyId = apiKeyId });

        return permissions.ToList();
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApiKeys] SET [LastUsedAt] = GETUTCDATE()
            WHERE [Id] = @Id",
            new { Id = apiKeyId });
    }

    private record ApiKeyDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string KeyPrefix { get; init; } = string.Empty;
        public string KeyHash { get; init; } = string.Empty;
        public string Environment { get; init; } = "production";
        public int RateLimitPerMinute { get; init; }
        public int RateLimitPerDay { get; init; }
        public string? AllowedIps { get; init; }
        public string? AllowedOrigins { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime? LastUsedAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? RevokedBy { get; init; }
        public string? RevokeReason { get; init; }

        public ApiKey ToEntity() => new(
            Id,
            ApplicationId,
            Name,
            Description,
            KeyPrefix,
            KeyHash,
            Environment,
            RateLimitPerMinute,
            RateLimitPerDay,
            AllowedIps,
            AllowedOrigins,
            CreatedAt,
            CreatedBy,
            ExpiresAt,
            LastUsedAt,
            RevokedAt,
            RevokedBy,
            RevokeReason);
    }
}
