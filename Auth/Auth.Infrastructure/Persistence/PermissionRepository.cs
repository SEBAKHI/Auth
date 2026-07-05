using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the permission repository.
/// </summary>
public class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PermissionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<PermissionDto>(
            "SELECT * FROM [dbo].[Permissions] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<PermissionDto>(@"
            SELECT * FROM [dbo].[Permissions]
            WHERE [Code] = @Code",
            new { Code = code.ToLowerInvariant() });

        return dto?.ToEntity();
    }

    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.Permissions.Name, ["[Name]"]),
        (SortFields.Permissions.Code, ["[Code]"]),
        (SortFields.Permissions.Description, ["[Description]"]),
        (SortFields.Permissions.Level, ["[Level]"]),
        (SortFields.Permissions.IsWildcard, ["[IsWildcard]"]),
        (SortFields.Permissions.IsActive, ["[IsActive]"]),
        (SortFields.Permissions.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Permissions.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetAllAsync(
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[Level], [Name]", "[Id]");
        var dtos = await connection.QueryAsync<PermissionDto>($@"
            SELECT * FROM [dbo].[Permissions]
            WHERE [IsActive] = 1
            ORDER BY {orderBy}");

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[Level], [Name]", "[Id]");
        var dtos = await connection.QueryAsync<PermissionDto>($@"
            SELECT * FROM [dbo].[Permissions]
            WHERE [ApplicationId] = @ApplicationId AND [IsActive] = 1
            ORDER BY {orderBy}",
            new { ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetByLevelAsync(
        byte level,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<PermissionDto>(@"
            SELECT * FROM [dbo].[Permissions]
            WHERE [Level] = @Level AND [IsActive] = 1
            ORDER BY [Name]",
            new { Level = level });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetChildPermissionsAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<PermissionDto>(@"
            SELECT * FROM [dbo].[Permissions]
            WHERE [ParentId] = @ParentId AND [IsActive] = 1
            ORDER BY [Name]",
            new { ParentId = parentId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Get permissions from roles and direct grants
        var permissions = await connection.QueryAsync<string>(@"
            WITH RolePermissions AS (
                -- Permissions from roles
                SELECT DISTINCT p.[Code]
                FROM [dbo].[Permissions] p
                INNER JOIN [dbo].[RolePermissions] rp ON p.[Id] = rp.[PermissionId]
                INNER JOIN [dbo].[UserRoles] ur ON rp.[RoleId] = ur.[RoleId]
                WHERE ur.[UserId] = @UserId
                  AND ur.[IsActive] = 1
                  AND p.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
            ),
            DirectPermissions AS (
                -- Direct user permission grants
                SELECT DISTINCT p.[Code]
                FROM [dbo].[Permissions] p
                INNER JOIN [dbo].[UserPermissions] up ON p.[Id] = up.[PermissionId]
                WHERE up.[UserId] = @UserId
                  AND up.[IsActive] = 1
                  AND p.[IsActive] = 1
                  AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())
            )
            SELECT [Code] FROM RolePermissions
            UNION
            SELECT [Code] FROM DirectPermissions",
            new { UserId = userId });

        return permissions.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var permissions = await connection.QueryAsync<string>(@"
            WITH RolePermissions AS (
                SELECT DISTINCT p.[Code]
                FROM [dbo].[Permissions] p
                INNER JOIN [dbo].[RolePermissions] rp ON p.[Id] = rp.[PermissionId]
                INNER JOIN [dbo].[Roles] r ON rp.[RoleId] = r.[Id]
                INNER JOIN [dbo].[UserRoles] ur ON r.[Id] = ur.[RoleId]
                WHERE ur.[UserId] = @UserId
                  AND r.[ApplicationId] = @ApplicationId
                  AND ur.[IsActive] = 1
                  AND r.[IsActive] = 1
                  AND p.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
            ),
            DirectPermissions AS (
                SELECT DISTINCT p.[Code]
                FROM [dbo].[Permissions] p
                INNER JOIN [dbo].[UserPermissions] up ON p.[Id] = up.[PermissionId]
                WHERE up.[UserId] = @UserId
                  AND (up.[ApplicationId] = @ApplicationId OR up.[ApplicationId] IS NULL)
                  AND up.[IsActive] = 1
                  AND p.[IsActive] = 1
                  AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())
            )
            SELECT [Code] FROM RolePermissions
            UNION
            SELECT [Code] FROM DirectPermissions",
            new { UserId = userId, ApplicationId = applicationId });

        return permissions.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> UserHasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var permissions = await GetUserEffectivePermissionsAsync(userId, cancellationToken);

        // Check for exact match or wildcard match
        return permissions.Any(p => PermissionMatches(p, permissionCode));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Permissions]
            WHERE [Code] = @Code",
            new { Code = code.ToLowerInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<Permission> CreateAsync(Permission permission, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Permissions] (
                [Id], [ApplicationId], [Code], [Name], [Description],
                [ParentId], [Level], [IsWildcard], [IsActive],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @ApplicationId, @Code, @Name, @Description,
                @ParentId, @Level, @IsWildcard, @IsActive,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                permission.Id,
                permission.ApplicationId,
                Code = permission.Code.Value,
                permission.Name,
                permission.Description,
                permission.ParentId,
                permission.Level,
                permission.IsWildcard,
                permission.IsActive,
                permission.CreatedAt,
                permission.CreatedBy,
                permission.ModifiedAt,
                permission.ModifiedBy
            });

        return permission;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Permission permission, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Permissions] SET
                [Name] = @Name,
                [Description] = @Description,
                [IsActive] = @IsActive,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                permission.Id,
                permission.Name,
                permission.Description,
                permission.IsActive,
                permission.ModifiedAt,
                permission.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Only delete if not a wildcard permission (system permissions)
        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[Permissions] WHERE [Id] = @Id AND [IsWildcard] = 0",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task GrantToRoleAsync(RolePermission rolePermission, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[RolePermissions] (
                [Id], [RoleId], [PermissionId], [GrantedAt], [GrantedBy]
            ) VALUES (
                @Id, @RoleId, @PermissionId, @GrantedAt, @GrantedBy
            )",
            new
            {
                rolePermission.Id,
                rolePermission.RoleId,
                rolePermission.PermissionId,
                rolePermission.GrantedAt,
                rolePermission.GrantedBy
            });
    }

    /// <inheritdoc />
    public async Task RevokeFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[RolePermissions]
            WHERE [RoleId] = @RoleId AND [PermissionId] = @PermissionId",
            new { RoleId = roleId, PermissionId = permissionId });
    }

    /// <inheritdoc />
    public async Task GrantToUserAsync(UserPermission userPermission, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserPermissions] (
                [Id], [UserId], [PermissionId], [ApplicationId], [GrantedAt], [GrantedBy], [ExpiresAt], [IsActive]
            ) VALUES (
                @Id, @UserId, @PermissionId, @ApplicationId, @GrantedAt, @GrantedBy, @ExpiresAt, @IsActive
            )",
            new
            {
                userPermission.Id,
                userPermission.UserId,
                userPermission.PermissionId,
                userPermission.ApplicationId,
                userPermission.GrantedAt,
                userPermission.GrantedBy,
                userPermission.ExpiresAt,
                userPermission.IsActive
            });
    }

    /// <inheritdoc />
    public async Task RevokeFromUserAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserPermissions] SET [IsActive] = 0
            WHERE [UserId] = @UserId AND [PermissionId] = @PermissionId",
            new { UserId = userId, PermissionId = permissionId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<PermissionDto>(@"
            SELECT p.* FROM [dbo].[Permissions] p
            INNER JOIN [dbo].[RolePermissions] rp ON p.[Id] = rp.[PermissionId]
            WHERE rp.[RoleId] = @RoleId AND p.[IsActive] = 1
            ORDER BY p.[Level], p.[Name]",
            new { RoleId = roleId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    #region Permission Implications

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionImplication>> GetImplicationsAsync(Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<PermissionImplicationDto>(@"
            SELECT * FROM [dbo].[PermissionImplications]
            WHERE [PermissionId] = @PermissionId",
            new { PermissionId = permissionId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionImplication>> GetImpliedByAsync(Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<PermissionImplicationDto>(@"
            SELECT * FROM [dbo].[PermissionImplications]
            WHERE [ImpliedPermissionId] = @PermissionId",
            new { PermissionId = permissionId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<PermissionImplication> AddImplicationAsync(PermissionImplication implication, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PermissionImplications] (
                [Id], [PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]
            ) VALUES (
                @Id, @PermissionId, @ImpliedPermissionId, @CreatedAt, @CreatedBy
            )",
            new
            {
                implication.Id,
                implication.PermissionId,
                implication.ImpliedPermissionId,
                implication.CreatedAt,
                implication.CreatedBy
            });

        return implication;
    }

    /// <inheritdoc />
    public async Task RemoveImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[PermissionImplications]
            WHERE [PermissionId] = @PermissionId AND [ImpliedPermissionId] = @ImpliedPermissionId",
            new { PermissionId = permissionId, ImpliedPermissionId = impliedPermissionId });
    }

    /// <inheritdoc />
    public async Task<bool> ImplicationExistsAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[PermissionImplications]
            WHERE [PermissionId] = @PermissionId AND [ImpliedPermissionId] = @ImpliedPermissionId",
            new { PermissionId = permissionId, ImpliedPermissionId = impliedPermissionId });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> WouldCreateCircularImplicationAsync(Guid permissionId, Guid impliedPermissionId, CancellationToken cancellationToken)
    {
        // If permissionId == impliedPermissionId, it's directly circular
        if (permissionId == impliedPermissionId)
            return true;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Check if adding this implication would create a cycle using recursive CTE
        var wouldCreateCycle = await connection.ExecuteScalarAsync<int>(@"
            ;WITH ImplicationChain AS (
                -- Start from the permission we're about to imply
                SELECT [ImpliedPermissionId]
                FROM [dbo].[PermissionImplications]
                WHERE [PermissionId] = @ImpliedPermissionId

                UNION ALL

                -- Follow the chain of implications
                SELECT pi.[ImpliedPermissionId]
                FROM [dbo].[PermissionImplications] pi
                INNER JOIN ImplicationChain ic ON pi.[PermissionId] = ic.[ImpliedPermissionId]
            )
            SELECT COUNT(1) FROM ImplicationChain
            WHERE [ImpliedPermissionId] = @PermissionId",
            new { PermissionId = permissionId, ImpliedPermissionId = impliedPermissionId });

        return wouldCreateCycle > 0;
    }

    #endregion

    #region Paginated Queries

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Permission> Permissions, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? applicationId,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var whereClause = "WHERE 1=1";
        if (applicationId.HasValue)
            whereClause += " AND [ApplicationId] = @ApplicationId";
        if (!string.IsNullOrWhiteSpace(search))
            whereClause += " AND ([Code] LIKE @Search OR [Name] LIKE @Search OR [Description] LIKE @Search)";
        if (isActive.HasValue)
            whereClause += " AND [IsActive] = @IsActive";

        var countSql = $"SELECT COUNT(1) FROM [dbo].[Permissions] {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
        {
            ApplicationId = applicationId,
            Search = $"%{search}%",
            IsActive = isActive
        });

        var offset = (pageNumber - 1) * pageSize;
        var dataSql = $@"
            SELECT * FROM [dbo].[Permissions]
            {whereClause}
            ORDER BY [Level], [Code]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var dtos = await connection.QueryAsync<PermissionDto>(dataSql, new
        {
            ApplicationId = applicationId,
            Search = $"%{search}%",
            IsActive = isActive,
            Offset = offset,
            PageSize = pageSize
        });

        var permissions = dtos.Select(dto => dto.ToEntity()).ToList();
        return (permissions, totalCount);
    }

    #endregion

    /// <summary>
    /// Checks if a permission code matches a required permission using wildcard logic.
    /// </summary>
    private static bool PermissionMatches(string heldPermission, string requiredPermission)
    {
        // Global wildcard grants everything
        if (heldPermission == "*")
            return true;

        // Exact match
        if (string.Equals(heldPermission, requiredPermission, StringComparison.OrdinalIgnoreCase))
            return true;

        // Wildcard matching (e.g., "crm:*" matches "crm:leads:read")
        if (heldPermission.EndsWith(":*"))
        {
            var prefix = heldPermission[..^2]; // Remove ":*"
            return requiredPermission.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(requiredPermission, prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static readonly IReadOnlyDictionary<string, string[]> PermissionUserSortColumns = SortSql.Map(
        (SortFields.PermissionUsers.Email, ["u.[Email]"]),
        (SortFields.PermissionUsers.FirstName, ["u.[FirstName]", "u.[LastName]"]),
        (SortFields.PermissionUsers.LastName, ["u.[LastName]", "u.[FirstName]"]),
        (SortFields.PermissionUsers.DisplayName, ["u.[FullName]"]),
        (SortFields.PermissionUsers.Status, ["u.[Status]"]),
        (SortFields.PermissionUsers.LastLoginAt, ["u.[LastLoginUtc]"]),
        (SortFields.PermissionUsers.CreatedAt, ["u.[CreatedAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PermissionUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid permissionId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var offset = (pageNumber - 1) * pageSize;
        var searchPattern = string.IsNullOrEmpty(searchTerm) ? null : $"%{searchTerm}%";
        var orderBy = SortSql.OrderBy(
            PermissionUserSortColumns, sortBy, sortDirection, "u.[Email]", "u.[Id]");

        // The grant flags are computed once in the CROSS APPLY and shared by
        // the WHERE filter and the SELECT list.
        const string fromClause = @"
            FROM [dbo].[Users] u
            CROSS APPLY (SELECT
                CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[UserPermissions] up
                    WHERE up.[UserId] = u.[Id]
                      AND up.[PermissionId] = @PermissionId
                      AND up.[IsActive] = 1
                      AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE()))
                    THEN 1 ELSE 0 END AS [ViaDirect],
                CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[OrganizationUserPermissions] oup
                    WHERE oup.[UserId] = u.[Id]
                      AND oup.[PermissionId] = @PermissionId
                      AND oup.[IsActive] = 1
                      AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE()))
                    THEN 1 ELSE 0 END AS [ViaOrganization],
                CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[RolePermissions] rp
                    WHERE rp.[PermissionId] = @PermissionId
                      AND (
                          EXISTS (
                              SELECT 1 FROM [dbo].[UserRoles] ur
                              WHERE ur.[UserId] = u.[Id]
                                AND ur.[RoleId] = rp.[RoleId]
                                AND ur.[IsActive] = 1
                                AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE()))
                          OR EXISTS (
                              SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                              WHERE our.[UserId] = u.[Id]
                                AND our.[RoleId] = rp.[RoleId]
                                AND our.[IsActive] = 1
                                AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))
                      ))
                    THEN 1 ELSE 0 END AS [ViaRole]
            ) f
            WHERE u.[IsDeleted] = 0
              AND (@SearchPattern IS NULL OR
                   u.[Email] LIKE @SearchPattern OR
                   u.[FirstName] LIKE @SearchPattern OR
                   u.[LastName] LIKE @SearchPattern)
              AND (f.[ViaDirect] = 1 OR f.[ViaOrganization] = 1 OR f.[ViaRole] = 1)";

        var sql = $@"
            SELECT COUNT(1) {fromClause};

            SELECT
                u.[Id] AS [UserId],
                u.[Email],
                u.[FirstName],
                u.[LastName],
                u.[FullName] AS [DisplayName],
                u.[Status],
                u.[LastLoginUtc] AS [LastLoginAt],
                u.[CreatedAt],
                CAST(f.[ViaDirect] AS BIT) AS [ViaDirect],
                CAST(f.[ViaOrganization] AS BIT) AS [ViaOrganization],
                CAST(f.[ViaRole] AS BIT) AS [ViaRole],
                (
                    SELECT STRING_AGG(x.[Name], ', ') WITHIN GROUP (ORDER BY x.[Name])
                    FROM (
                        SELECT DISTINCT r.[Name]
                        FROM [dbo].[RolePermissions] rp
                        INNER JOIN [dbo].[Roles] r ON rp.[RoleId] = r.[Id]
                        WHERE rp.[PermissionId] = @PermissionId
                          AND (
                              EXISTS (
                                  SELECT 1 FROM [dbo].[UserRoles] ur
                                  WHERE ur.[UserId] = u.[Id]
                                    AND ur.[RoleId] = rp.[RoleId]
                                    AND ur.[IsActive] = 1
                                    AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE()))
                              OR EXISTS (
                                  SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                                  WHERE our.[UserId] = u.[Id]
                                    AND our.[RoleId] = rp.[RoleId]
                                    AND our.[IsActive] = 1
                                    AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))
                          )
                    ) x
                ) AS [RoleNames]
            {fromClause}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            PermissionId = permissionId,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var users = (await multi.ReadAsync<PermissionUserRow>()).ToList();

        return (users, totalCount);
    }

    private record PermissionDto
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public Guid? ParentId { get; init; }
        public byte Level { get; init; }
        public bool IsWildcard { get; init; }
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public Permission ToEntity() => new(
            Id,
            ApplicationId,
            Code,
            Name,
            Description,
            ParentId,
            Level,
            IsWildcard,
            IsActive,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record PermissionImplicationDto
    {
        public Guid Id { get; init; }
        public Guid PermissionId { get; init; }
        public Guid ImpliedPermissionId { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }

        public PermissionImplication ToEntity() => new(
            Id,
            PermissionId,
            ImpliedPermissionId,
            CreatedAt,
            CreatedBy);
    }
}
