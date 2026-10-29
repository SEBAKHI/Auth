using System.Data;
using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;

namespace Auth.Infrastructure.Persistence;

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
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApiKeyDto>(
            "SELECT * FROM [dbo].[ApiKeys] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // A key only validates while its owning application is alive: joining
        // Applications closes the hole where keys of inactive or soft-deleted
        // applications kept authenticating.
        var dto = await connection.QueryFirstOrDefaultAsync<ApiKeyDto>(@"
            SELECT k.* FROM [dbo].[ApiKeys] k
            INNER JOIN [dbo].[Applications] a ON a.[Id] = k.[ApplicationId]
            WHERE k.[KeyHash] = @KeyHash AND k.[RevokedAt] IS NULL
              AND a.[IsActive] = 1 AND a.[IsDeleted] = 0",
            new { KeyHash = keyHash });

        return dto?.ToEntity();
    }

    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.ApiKeys.Name, ["[Name]"]),
        (SortFields.ApiKeys.Description, ["[Description]"]),
        (SortFields.ApiKeys.KeyPrefix, ["[KeyPrefix]"]),
        (SortFields.ApiKeys.Environment, ["[Environment]"]),
        (SortFields.ApiKeys.RateLimitPerMinute, ["[RateLimitPerMinute]"]),
        (SortFields.ApiKeys.RateLimitPerDay, ["[RateLimitPerDay]"]),
        (SortFields.ApiKeys.CreatedAt, ["[CreatedAt]"]),
        (SortFields.ApiKeys.ExpiresAt, ["[ExpiresAt]"]),
        (SortFields.ApiKeys.LastUsedAt, ["[LastUsedAt]"]),
        (SortFields.ApiKeys.RevokedAt, ["[RevokedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> ListAsync(
        Guid? applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[CreatedAt] DESC", "[Id]");

        // Branched rather than a single "@ApplicationId IS NULL OR [ApplicationId] = @ApplicationId":
        // that predicate is not sargable and would cost the single-application path its
        // IX_ApiKeys_ApplicationId seek. Both fragments are constants; nothing is concatenated
        // from user input (orderBy comes from the SortColumns allow-list).
        var where = applicationId.HasValue ? "WHERE [ApplicationId] = @ApplicationId" : string.Empty;

        var dtos = await connection.QueryAsync<ApiKeyDto>($@"
            SELECT * FROM [dbo].[ApiKeys]
            {where}
            ORDER BY {orderBy}",
            new { ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<ApiKey> CreateAsync(
        ApiKey apiKey,
        IReadOnlyCollection<ApiKeyScope> scopes,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await InsertKeyAsync(connection, transaction, apiKey, cancellationToken);
        await InsertScopesAsync(connection, transaction, scopes, cancellationToken);

        transaction.Commit();
        return apiKey;
    }

    /// <inheritdoc />
    public async Task RotateAsync(
        ApiKey replaced,
        ApiKey replacement,
        IReadOnlyCollection<ApiKeyScope> scopes,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await InsertKeyAsync(connection, transaction, replacement, cancellationToken);
        await InsertScopesAsync(connection, transaction, scopes, cancellationToken);

        // Only the rotated key's end moves; a revocation that landed meanwhile stays.
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[ApiKeys] SET [ExpiresAt] = @ExpiresAt
            WHERE [Id] = @Id AND [RevokedAt] IS NULL",
            new { replaced.Id, replaced.ExpiresAt },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
    }

    private static Task InsertKeyAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ApiKey apiKey,
        CancellationToken cancellationToken) =>
        // RateLimitPerMinute, RateLimitPerDay and AllowedOrigins are left to their column
        // defaults: nothing enforced them (owner decision D-128-3).
        connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO [dbo].[ApiKeys] (
                [Id], [ApplicationId], [Name], [Description], [KeyPrefix], [KeyHash],
                [Environment], [AllowedIps], [CreatedAt], [CreatedBy], [ExpiresAt], [KeySelector]
            ) VALUES (
                @Id, @ApplicationId, @Name, @Description, @KeyPrefix, @KeyHash,
                @Environment, @AllowedIps, @CreatedAt, @CreatedBy, @ExpiresAt, @KeySelector
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
                AllowedIps = SerializeAllowedIps(apiKey.AllowedIps),
                apiKey.CreatedAt,
                apiKey.CreatedBy,
                apiKey.ExpiresAt,
                apiKey.KeySelector
            },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task InsertScopesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<ApiKeyScope> scopes,
        CancellationToken cancellationToken)
    {
        foreach (var scope in scopes)
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
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
                },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    /// <summary>The address list as the column holds it: a JSON array, or NULL for any address.</summary>
    internal static string? SerializeAllowedIps(IpAllowList allowedIps) =>
        allowedIps.Entries.Count == 0 ? null : JsonSerializer.Serialize(allowedIps.Entries);

    internal static IpAllowList DeserializeAllowedIps(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return IpAllowList.Any;
        }

        try
        {
            return IpAllowList.FromStored(JsonSerializer.Deserialize<string[]>(json));
        }
        catch (JsonException)
        {
            // A value this code never wrote: matching nothing is the safe reading of a list
            // that cannot be read, never "any address".
            return IpAllowList.FromStored([json]);
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApiKeys] SET
                [ExpiresAt] = @ExpiresAt,
                [LastUsedAt] = @LastUsedAt,
                [RevokedAt] = @RevokedAt,
                [RevokedBy] = @RevokedBy,
                [RevokeReason] = @RevokeReason
            WHERE [Id] = @Id",
            new
            {
                apiKey.Id,
                apiKey.ExpiresAt,
                apiKey.LastUsedAt,
                apiKey.RevokedAt,
                apiKey.RevokedBy,
                apiKey.RevokeReason
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
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
    public async Task RemoveScopeAsync(Guid apiKeyId, Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[ApiKeyScopes]
            WHERE [ApiKeyId] = @ApiKeyId AND [PermissionId] = @PermissionId",
            new { ApiKeyId = apiKeyId, PermissionId = permissionId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetScopesAsync(Guid apiKeyId, CancellationToken cancellationToken)
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
    public async Task<IReadOnlyList<Guid>> GetScopePermissionIdsAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(@"
            SELECT [PermissionId] FROM [dbo].[ApiKeyScopes] WHERE [ApiKeyId] = @ApiKeyId",
            new { ApiKeyId = apiKeyId },
            cancellationToken: cancellationToken));

        return ids.ToList();
    }

    /// <summary>
    /// Dapper expands an IN list into one parameter per element and SQL Server caps a
    /// batch at 2100, so a platform-wide listing has to be chunked rather than trusted
    /// to stay small.
    /// </summary>
    private const int ScopeLookupChunkSize = 1000;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetScopesAsync(
        IReadOnlyCollection<Guid> apiKeyIds,
        CancellationToken cancellationToken)
    {
        if (apiKeyIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var scopesByKey = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var chunk in apiKeyIds.Distinct().Chunk(ScopeLookupChunkSize))
        {
            var rows = await connection.QueryAsync<ScopeRow>(@"
                SELECT aks.[ApiKeyId], p.[Code]
                FROM [dbo].[ApiKeyScopes] aks
                INNER JOIN [dbo].[Permissions] p ON p.[Id] = aks.[PermissionId]
                WHERE aks.[ApiKeyId] IN @ApiKeyIds AND p.[IsActive] = 1",
                new { ApiKeyIds = chunk });

            foreach (var group in rows.GroupBy(row => row.ApiKeyId))
            {
                scopesByKey[group.Key] = group.Select(row => row.Code).ToList();
            }
        }

        return scopesByKey;
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApiKeys] SET [LastUsedAt] = GETUTCDATE()
            WHERE [Id] = @Id",
            new { Id = apiKeyId });
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetActiveBySelectorAsync(string selector, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One row at most (UX_ApiKeys_KeySelector), and only a key of a live (active, not
        // soft-deleted) application may authenticate.
        var dto = await connection.QueryFirstOrDefaultAsync<ApiKeyDto>(new CommandDefinition(@"
            SELECT k.* FROM [dbo].[ApiKeys] k
            INNER JOIN [dbo].[Applications] a ON a.[Id] = k.[ApplicationId]
            WHERE k.[KeySelector] = @Selector
              AND k.[RevokedAt] IS NULL
              AND (k.[ExpiresAt] IS NULL OR k.[ExpiresAt] > GETUTCDATE())
              AND a.[IsActive] = 1 AND a.[IsDeleted] = 0",
            new { Selector = selector },
            cancellationToken: cancellationToken));

        return dto?.ToEntity();
    }

    private record ScopeRow
    {
        public Guid ApiKeyId { get; init; }
        public string Code { get; init; } = string.Empty;
    }

    private record ApiKeyDto
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string KeyPrefix { get; init; } = string.Empty;
        public string KeyHash { get; init; } = string.Empty;
        public string? KeySelector { get; init; }
        public string Environment { get; init; } = "production";
        public string? AllowedIps { get; init; }
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
            KeySelector,
            Environment,
            DeserializeAllowedIps(AllowedIps),
            CreatedAt,
            CreatedBy,
            ExpiresAt,
            LastUsedAt,
            RevokedAt,
            RevokedBy,
            RevokeReason);
    }
}
