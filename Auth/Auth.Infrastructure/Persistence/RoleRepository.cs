using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the role repository.
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RoleRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<RoleDto>(
            "SELECT * FROM [dbo].[Roles] WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<Role?> GetByCodeAsync(Guid applicationId, string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<RoleDto>(@"
            SELECT * FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId AND [Code] = @Code",
            new { ApplicationId = applicationId, Code = code.ToUpperInvariant() });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<Role?> GetByCodeAsync(Guid? applicationId, string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<RoleDto>(@"
            SELECT * FROM [dbo].[Roles]
            WHERE (@ApplicationId IS NULL AND [ApplicationId] IS NULL OR [ApplicationId] = @ApplicationId)
              AND [Code] = @Code",
            new { ApplicationId = applicationId, Code = code.ToUpperInvariant() });

        return dto?.ToEntity();
    }

    private static readonly IReadOnlyDictionary<string, string[]> SortColumns = SortSql.Map(
        (SortFields.Roles.Name, ["[Name]"]),
        (SortFields.Roles.Code, ["[Code]"]),
        (SortFields.Roles.Description, ["[Description]"]),
        (SortFields.Roles.IsSystem, ["[IsSystem]"]),
        (SortFields.Roles.IsActive, ["[IsActive]"]),
        (SortFields.Roles.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Roles.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetAllAsync(
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[Name]", "[Id]");
        var dtos = await connection.QueryAsync<RoleDto>($@"
            SELECT * FROM [dbo].[Roles]
            WHERE [IsActive] = 1
            ORDER BY {orderBy}");

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(SortColumns, sortBy, sortDirection, "[Name]", "[Id]");
        var dtos = await connection.QueryAsync<RoleDto>($@"
            SELECT * FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId AND [IsActive] = 1
            ORDER BY {orderBy}",
            new { ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<RoleDto>(@"
            SELECT r.* FROM [dbo].[Roles] r
            INNER JOIN [dbo].[UserRoles] ur ON r.[Id] = ur.[RoleId]
            WHERE ur.[UserId] = @UserId
              AND ur.[IsActive] = 1
              AND r.[IsActive] = 1
              AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
            ORDER BY r.[Name]",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetUserRolesForApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<RoleDto>(@"
            SELECT r.* FROM [dbo].[Roles] r
            INNER JOIN [dbo].[UserRoles] ur ON r.[Id] = ur.[RoleId]
            WHERE ur.[UserId] = @UserId
              AND r.[ApplicationId] = @ApplicationId
              AND ur.[IsActive] = 1
              AND r.[IsActive] = 1
              AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
            ORDER BY r.[Name]",
            new { UserId = userId, ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(Guid applicationId, string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId AND [Code] = @Code",
            new { ApplicationId = applicationId, Code = code.ToUpperInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<Role> CreateAsync(Role role, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Roles] (
                [Id], [ApplicationId], [Code], [Name], [Description],
                [IsActive], [IsSystem],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @ApplicationId, @Code, @Name, @Description,
                @IsActive, @IsSystem,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                role.Id,
                role.ApplicationId,
                role.Code,
                role.Name,
                role.Description,
                role.IsActive,
                role.IsSystem,
                role.CreatedAt,
                role.CreatedBy,
                role.ModifiedAt,
                role.ModifiedBy
            });

        return role;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Role role, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Roles] SET
                [Name] = @Name,
                [Description] = @Description,
                [IsActive] = @IsActive,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                role.Id,
                role.Name,
                role.Description,
                role.IsActive,
                role.ModifiedAt,
                role.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            "DELETE FROM [dbo].[Roles] WHERE [Id] = @Id AND [IsSystem] = 0",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task AssignToUserAsync(UserRole userRole, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[UserRoles] (
                [Id], [UserId], [RoleId], [ApplicationId], [AssignedAt], [AssignedBy], [ExpiresAt], [IsActive]
            ) VALUES (
                @Id, @UserId, @RoleId, @ApplicationId, @AssignedAt, @AssignedBy, @ExpiresAt, @IsActive
            )",
            new
            {
                userRole.Id,
                userRole.UserId,
                userRole.RoleId,
                userRole.ApplicationId,
                userRole.AssignedAt,
                userRole.AssignedBy,
                userRole.ExpiresAt,
                userRole.IsActive
            });
    }

    /// <inheritdoc />
    public async Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Hard delete: UQ_UserRoles spans (UserId, RoleId, ApplicationId) without an
        // [IsActive] filter, so a deactivated row would block re-assigning the same
        // role later. Removals are recorded in the audit log, not in this table.
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId AND [RoleId] = @RoleId",
            new { UserId = userId, RoleId = roleId });
    }

    /// <inheritdoc />
    public async Task<bool> UserHasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId
              AND [RoleId] = @RoleId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, RoleId = roleId });

        return count > 0;
    }

    private static readonly IReadOnlyDictionary<string, string[]> RoleUserSortColumns = SortSql.Map(
        (SortFields.RoleUsers.Email, ["u.[Email]"]),
        (SortFields.RoleUsers.FirstName, ["u.[FirstName]", "u.[LastName]"]),
        (SortFields.RoleUsers.LastName, ["u.[LastName]", "u.[FirstName]"]),
        (SortFields.RoleUsers.DisplayName, ["u.[FullName]"]),
        (SortFields.RoleUsers.Status, ["u.[Status]"]),
        (SortFields.RoleUsers.LastLoginAt, ["u.[LastLoginUtc]"]),
        (SortFields.RoleUsers.CreatedAt, ["u.[CreatedAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<RoleUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid roleId,
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
            RoleUserSortColumns, sortBy, sortDirection, "u.[Email]", "u.[Id]");

        // The assignment flags are computed once in the CROSS APPLY and shared
        // by the WHERE filter and the SELECT list.
        const string fromClause = @"
            FROM [dbo].[Users] u
            CROSS APPLY (SELECT
                CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[UserRoles] ur
                    WHERE ur.[UserId] = u.[Id]
                      AND ur.[RoleId] = @RoleId
                      AND ur.[IsActive] = 1
                      AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE()))
                    THEN 1 ELSE 0 END AS [ViaDirect],
                CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                    WHERE our.[UserId] = u.[Id]
                      AND our.[RoleId] = @RoleId
                      AND our.[IsActive] = 1
                      AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))
                    THEN 1 ELSE 0 END AS [ViaOrganization]
            ) f
            WHERE u.[IsDeleted] = 0
              AND (@SearchPattern IS NULL OR
                   u.[Email] LIKE @SearchPattern OR
                   u.[FirstName] LIKE @SearchPattern OR
                   u.[LastName] LIKE @SearchPattern)
              AND (f.[ViaDirect] = 1 OR f.[ViaOrganization] = 1)";

        var sql = $@"
            SELECT COUNT(1) {fromClause};

            SELECT
                u.[Id] AS [UserId],
                u.[Email],
                u.[FirstName],
                u.[LastName],
                u.[FullName] AS [DisplayName],
                u.[ProfileImageUrl],
                u.[Status],
                u.[LastLoginUtc] AS [LastLoginAt],
                u.[CreatedAt],
                CAST(f.[ViaDirect] AS BIT) AS [ViaDirect],
                CAST(f.[ViaOrganization] AS BIT) AS [ViaOrganization],
                (
                    SELECT STRING_AGG(x.[Name], ', ') WITHIN GROUP (ORDER BY x.[Name])
                    FROM (
                        SELECT DISTINCT og.[Name]
                        FROM [dbo].[OrganizationUserRoles] our
                        INNER JOIN [dbo].[Organizations] og ON our.[OrganizationId] = og.[Id]
                        WHERE our.[UserId] = u.[Id]
                          AND our.[RoleId] = @RoleId
                          AND our.[IsActive] = 1
                          AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
                    ) x
                ) AS [OrganizationNames]
            {fromClause}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            RoleId = roleId,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var users = (await multi.ReadAsync<RoleUserRow>()).ToList();

        return (users, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleApplicationRow>> GetRoleApplicationsAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<RoleApplicationRow>(@"
            SELECT
                a.[Id] AS [ApplicationId],
                a.[Code],
                a.[Name],
                a.[LogoUrl],
                a.[IsActive],
                CAST(MAX(src.[IsOwner]) AS BIT) AS [IsOwner],
                CAST(MAX(src.[IsAssigned]) AS BIT) AS [IsAssigned]
            FROM (
                SELECT r.[ApplicationId], 1 AS [IsOwner], 0 AS [IsAssigned]
                FROM [dbo].[Roles] r
                WHERE r.[Id] = @RoleId AND r.[ApplicationId] IS NOT NULL
                UNION ALL
                SELECT ur.[ApplicationId], 0, 1
                FROM [dbo].[UserRoles] ur
                WHERE ur.[RoleId] = @RoleId
                  AND ur.[ApplicationId] IS NOT NULL
                  AND ur.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
                UNION ALL
                SELECT our.[ApplicationId], 0, 1
                FROM [dbo].[OrganizationUserRoles] our
                WHERE our.[RoleId] = @RoleId
                  AND our.[IsActive] = 1
                  AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
            ) src
            INNER JOIN [dbo].[Applications] a ON src.[ApplicationId] = a.[Id]
            GROUP BY a.[Id], a.[Code], a.[Name], a.[LogoUrl], a.[IsActive]",
            new { RoleId = roleId });

        return rows.ToList();
    }

    private record RoleDto
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsActive { get; init; }
        public bool IsSystem { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public Role ToEntity() => new(
            Id,
            ApplicationId,
            Code,
            Name,
            Description,
            IsActive,
            IsSystem,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }
}
