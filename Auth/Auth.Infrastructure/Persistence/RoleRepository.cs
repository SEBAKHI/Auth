using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<RoleDto>(@"
            SELECT * FROM [dbo].[Roles]
            WHERE [IsActive] = 1
            ORDER BY [Name]");

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<RoleDto>(@"
            SELECT * FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId AND [IsActive] = 1
            ORDER BY [Name]",
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

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[UserRoles] SET [IsActive] = 0
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
