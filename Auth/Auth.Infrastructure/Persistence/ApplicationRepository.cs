using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the application repository.
/// </summary>
public class ApplicationRepository : IApplicationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ApplicationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<AppEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<AppEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Code] = @Code",
            new { Code = code.ToUpperInvariant() });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppEntity>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            ORDER BY [Code]");

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppEntity>> GetActiveAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [IsActive] = 1
            ORDER BY [Code]");

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Applications]
            WHERE [Code] = @Code",
            new { Code = code.ToUpperInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<AppEntity> CreateAsync(AppEntity application, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Applications] (
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Code, @Name, @Description, @BaseUrl, @LogoUrl, @ContactEmail,
                @IsActive, @AllowSelfRegistration, @RequireTwoFactor, @RequireEmailVerification,
                @SessionTimeoutMinutes, @MaxConcurrentSessions,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                application.Id,
                application.Code,
                application.Name,
                application.Description,
                application.BaseUrl,
                application.LogoUrl,
                application.ContactEmail,
                application.IsActive,
                application.AllowSelfRegistration,
                application.RequireTwoFactor,
                application.RequireEmailVerification,
                application.SessionTimeoutMinutes,
                application.MaxConcurrentSessions,
                application.CreatedAt,
                application.CreatedBy,
                application.ModifiedAt,
                application.ModifiedBy
            });

        return application;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AppEntity application, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Applications] SET
                [Name] = @Name,
                [Description] = @Description,
                [BaseUrl] = @BaseUrl,
                [LogoUrl] = @LogoUrl,
                [ContactEmail] = @ContactEmail,
                [IsActive] = @IsActive,
                [AllowSelfRegistration] = @AllowSelfRegistration,
                [RequireTwoFactor] = @RequireTwoFactor,
                [RequireEmailVerification] = @RequireEmailVerification,
                [SessionTimeoutMinutes] = @SessionTimeoutMinutes,
                [MaxConcurrentSessions] = @MaxConcurrentSessions,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                application.Id,
                application.Name,
                application.Description,
                application.BaseUrl,
                application.LogoUrl,
                application.ContactEmail,
                application.IsActive,
                application.AllowSelfRegistration,
                application.RequireTwoFactor,
                application.RequireEmailVerification,
                application.SessionTimeoutMinutes,
                application.MaxConcurrentSessions,
                application.ModifiedAt,
                application.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Hard delete for applications (could be changed to soft delete if needed)
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[Applications]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AppEntity> Applications, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var whereClause = "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND ([Code] LIKE @Search OR [Name] LIKE @Search OR [Description] LIKE @Search)";
        }
        if (isActive.HasValue)
        {
            whereClause += " AND [IsActive] = @IsActive";
        }

        var countSql = $"SELECT COUNT(1) FROM [dbo].[Applications] {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
        {
            Search = $"%{search}%",
            IsActive = isActive
        });

        var offset = (pageNumber - 1) * pageSize;
        var dataSql = $@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            {whereClause}
            ORDER BY [Code]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var dtos = await connection.QueryAsync<ApplicationDto>(dataSql, new
        {
            Search = $"%{search}%",
            IsActive = isActive,
            Offset = offset,
            PageSize = pageSize
        });

        var applications = dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
        return (applications, totalCount);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveApiKeysAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[ApiKeys]
            WHERE [ApplicationId] = @ApplicationId
              AND [RevokedAt] IS NULL
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { ApplicationId = applicationId });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveUserAssignmentsAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[UserRoles]
            WHERE [ApplicationId] = @ApplicationId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { ApplicationId = applicationId });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveOrganizationsAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OrganizationApplications]
            WHERE [ApplicationId] = @ApplicationId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { ApplicationId = applicationId });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetRolesAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var roles = await connection.QueryAsync<RoleDto>(@"
            SELECT
                [Id], [ApplicationId], [Code], [Name], [Description],
                [IsSystem], [IsActive],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY [Code]",
            new { ApplicationId = applicationId });

        return roles.Select(r => r.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetPermissionsAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var permissions = await connection.QueryAsync<PermissionDto>(@"
            SELECT
                [Id], [ApplicationId], [ParentId], [Code], [Name], [Description],
                [Level], [IsWildcard], [IsActive],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Permissions]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY [Code]",
            new { ApplicationId = applicationId });

        return permissions.Select(p => p.ToEntity()).ToList().AsReadOnly();
    }

    // Internal DTO for mapping from database
    private record ApplicationDto
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? BaseUrl { get; init; }
        public string? LogoUrl { get; init; }
        public string? ContactEmail { get; init; }
        public bool IsActive { get; init; }
        public bool AllowSelfRegistration { get; init; }
        public bool RequireTwoFactor { get; init; }
        public bool RequireEmailVerification { get; init; }
        public int SessionTimeoutMinutes { get; init; }
        public int MaxConcurrentSessions { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public AppEntity ToEntity() => new(
            Id,
            Code,
            Name,
            Description,
            BaseUrl,
            LogoUrl,
            ContactEmail,
            IsActive,
            AllowSelfRegistration,
            RequireTwoFactor,
            RequireEmailVerification,
            SessionTimeoutMinutes,
            MaxConcurrentSessions,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record RoleDto
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsSystem { get; init; }
        public bool IsActive { get; init; }
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

    private record PermissionDto
    {
        public Guid Id { get; init; }
        public Guid? ApplicationId { get; init; }
        public Guid? ParentId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
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
}
