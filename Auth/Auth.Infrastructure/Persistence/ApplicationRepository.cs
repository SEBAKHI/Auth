using System.Data;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
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
                [SessionTimeoutMinutes], [MaxConcurrentSessions], [ReauthenticationMaxAgeMinutes],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Id] = @Id AND [IsDeleted] = 0",
            new { Id = id });

        var entity = dto?.ToEntity();
        if (entity is not null)
        {
            await LoadRedirectUrisAsync(connection, entity);
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<AppEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Historical name resolution only (audit views). No redirect-URI
        // hydration and no IsDeleted filter.
        var dto = await connection.QueryFirstOrDefaultAsync<ApplicationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions], [ReauthenticationMaxAgeMinutes],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy],
                [IsDeleted], [DeletedAt], [DeletedBy]
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
                [SessionTimeoutMinutes], [MaxConcurrentSessions], [ReauthenticationMaxAgeMinutes],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            WHERE [Code] = @Code AND [IsDeleted] = 0",
            new { Code = code.ToUpperInvariant() });

        var entity = dto?.ToEntity();
        if (entity is not null)
        {
            await LoadRedirectUrisAsync(connection, entity);
        }

        return entity;
    }

    private static async Task LoadRedirectUrisAsync(IDbConnection connection, AppEntity application)
    {
        var uris = await connection.QueryAsync<string>(@"
            SELECT [Uri]
            FROM [dbo].[ApplicationRedirectUris]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY [CreatedAt]",
            new { ApplicationId = application.Id });

        application.LoadRedirectUris(uris);
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
            WHERE [IsDeleted] = 0
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
            WHERE [IsActive] = 1 AND [IsDeleted] = 0
            ORDER BY [Code]");

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Deliberately includes soft-deleted rows: a deleted application's
        // code stays reserved and can never be claimed by a new application.
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
        using var transaction = connection.BeginTransaction();

        // Every configurable column is written here: a value the caller set and
        // the INSERT silently dropped would come back from the next read as the
        // column default, with nothing to show the setting was lost.
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Applications] (
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions], [ReauthenticationMaxAgeMinutes],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Code, @Name, @Description, @BaseUrl, @LogoUrl, @ContactEmail,
                @IsActive, @AllowSelfRegistration, @RequireTwoFactor, @RequireEmailVerification,
                @SessionTimeoutMinutes, @MaxConcurrentSessions, @ReauthenticationMaxAgeMinutes,
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
                application.ReauthenticationMaxAgeMinutes,
                application.CreatedAt,
                application.CreatedBy,
                application.ModifiedAt,
                application.ModifiedBy
            },
            transaction);

        // The redirect-URI allowlist is part of the application, so it lands in
        // the same transaction: never an application row whose allowlist is a
        // partial write.
        if (application.RedirectUris.Count > 0)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[ApplicationRedirectUris] ([ApplicationId], [Uri], [CreatedBy])
                VALUES (@ApplicationId, @Uri, @CreatedBy)",
                application.RedirectUris.Select(uri => new
                {
                    ApplicationId = application.Id,
                    Uri = uri,
                    application.CreatedBy
                }),
                transaction);
        }

        transaction.Commit();

        return application;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AppEntity application, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

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
                [ReauthenticationMaxAgeMinutes] = @ReauthenticationMaxAgeMinutes,
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
                application.ReauthenticationMaxAgeMinutes,
                application.ModifiedAt,
                application.ModifiedBy
            },
            transaction);

        // Sync the redirect-URI allowlist (delete + reinsert; the list is
        // capped at 20 entries so this stays trivial). Both statements share the
        // update's transaction — a failed reinsert must not leave the
        // application with an emptied allowlist, which would break every
        // authorization request for it.
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[ApplicationRedirectUris]
            WHERE [ApplicationId] = @ApplicationId",
            new { ApplicationId = application.Id },
            transaction);

        if (application.RedirectUris.Count > 0)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[ApplicationRedirectUris] ([ApplicationId], [Uri], [CreatedBy])
                VALUES (@ApplicationId, @Uri, @CreatedBy)",
                application.RedirectUris.Select(uri => new
                {
                    ApplicationId = application.Id,
                    Uri = uri,
                    CreatedBy = application.ModifiedBy ?? application.CreatedBy
                }),
                transaction);
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid deletedBy, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Soft delete: the row stays (child FKs keep their history) but the
        // application is deactivated and its credentials die with it, atomically.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Applications]
            SET [IsDeleted] = 1,
                [DeletedAt] = GETUTCDATE(),
                [DeletedBy] = @DeletedBy,
                [IsActive] = 0,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @DeletedBy
            WHERE [Id] = @Id AND [IsDeleted] = 0",
            new { Id = id, DeletedBy = deletedBy },
            transaction);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[ApiKeys]
            SET [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @DeletedBy,
                [RevokeReason] = N'Application deleted'
            WHERE [ApplicationId] = @Id AND [RevokedAt] IS NULL",
            new { Id = id, DeletedBy = deletedBy },
            transaction);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[WebhookKeys]
            SET [RevokedAt] = GETUTCDATE(),
                [RevokedBy] = @DeletedBy,
                [RevokeReason] = N'Application deleted'
            WHERE [ApplicationId] = @Id AND [RevokedAt] IS NULL",
            new { Id = id, DeletedBy = deletedBy },
            transaction);

        transaction.Commit();
    }

    private static readonly IReadOnlyDictionary<string, string[]> PagedSortColumns = SortSql.Map(
        (SortFields.Applications.Name, ["[Name]"]),
        (SortFields.Applications.Code, ["[Code]"]),
        (SortFields.Applications.Description, ["[Description]"]),
        (SortFields.Applications.BaseUrl, ["[BaseUrl]"]),
        (SortFields.Applications.ContactEmail, ["[ContactEmail]"]),
        (SortFields.Applications.Status, ["[IsActive]"]),
        (SortFields.Applications.IsActive, ["[IsActive]"]),
        (SortFields.Applications.AllowSelfRegistration, ["[AllowSelfRegistration]"]),
        (SortFields.Applications.RequireTwoFactor, ["[RequireTwoFactor]"]),
        (SortFields.Applications.RequireEmailVerification, ["[RequireEmailVerification]"]),
        (SortFields.Applications.SessionTimeoutMinutes, ["[SessionTimeoutMinutes]"]),
        (SortFields.Applications.MaxConcurrentSessions, ["[MaxConcurrentSessions]"]),
        (SortFields.Applications.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Applications.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AppEntity> Applications, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isActive,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var whereClause = "WHERE [IsDeleted] = 0";
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
        var orderBy = SortSql.OrderBy(
            PagedSortColumns, sortBy, sortDirection, "[Code]", "[Id]");
        var dataSql = $@"
            SELECT
                [Id], [Code], [Name], [Description], [BaseUrl], [LogoUrl], [ContactEmail],
                [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [RequireEmailVerification],
                [SessionTimeoutMinutes], [MaxConcurrentSessions],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Applications]
            {whereClause}
            ORDER BY {orderBy}
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

    private static readonly IReadOnlyDictionary<string, string[]> RoleSortColumns = SortSql.Map(
        (SortFields.Roles.Name, ["[Name]"]),
        (SortFields.Roles.Code, ["[Code]"]),
        (SortFields.Roles.Description, ["[Description]"]),
        (SortFields.Roles.IsSystem, ["[IsSystem]"]),
        (SortFields.Roles.IsActive, ["[IsActive]"]),
        (SortFields.Roles.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Roles.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetRolesAsync(
        Guid applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(RoleSortColumns, sortBy, sortDirection, "[Code]", "[Id]");
        var roles = await connection.QueryAsync<RoleDto>($@"
            SELECT
                [Id], [ApplicationId], [Code], [Name], [Description],
                [IsSystem], [IsActive],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Roles]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY {orderBy}",
            new { ApplicationId = applicationId });

        return roles.Select(r => r.ToEntity()).ToList().AsReadOnly();
    }

    private static readonly IReadOnlyDictionary<string, string[]> PermissionSortColumns = SortSql.Map(
        (SortFields.Permissions.Name, ["[Name]"]),
        (SortFields.Permissions.Code, ["[Code]"]),
        (SortFields.Permissions.Description, ["[Description]"]),
        (SortFields.Permissions.Level, ["[Level]"]),
        (SortFields.Permissions.IsWildcard, ["[IsWildcard]"]),
        (SortFields.Permissions.IsActive, ["[IsActive]"]),
        (SortFields.Permissions.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Permissions.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetPermissionsAsync(
        Guid applicationId,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var orderBy = SortSql.OrderBy(PermissionSortColumns, sortBy, sortDirection, "[Code]", "[Id]");
        var permissions = await connection.QueryAsync<PermissionDto>($@"
            SELECT
                [Id], [ApplicationId], [ParentId], [Code], [Name], [Description],
                [Level], [IsWildcard], [IsActive],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Permissions]
            WHERE [ApplicationId] = @ApplicationId
            ORDER BY {orderBy}",
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
        public int? ReauthenticationMaxAgeMinutes { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }
        public bool IsDeleted { get; init; }
        public DateTime? DeletedAt { get; init; }
        public Guid? DeletedBy { get; init; }

        public AppEntity ToEntity()
        {
            var entity = new AppEntity(
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
            entity.LoadReauthenticationMaxAge(ReauthenticationMaxAgeMinutes);
            entity.LoadDeletionState(IsDeleted, DeletedAt, DeletedBy);
            return entity;
        }
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

    private static readonly IReadOnlyDictionary<string, string[]> ApplicationUserSortColumns = SortSql.Map(
        (SortFields.ApplicationUsers.Email, ["u.[Email]"]),
        (SortFields.ApplicationUsers.FirstName, ["u.[FirstName]", "u.[LastName]"]),
        (SortFields.ApplicationUsers.LastName, ["u.[LastName]", "u.[FirstName]"]),
        (SortFields.ApplicationUsers.DisplayName, ["u.[FullName]"]),
        (SortFields.ApplicationUsers.Status, ["u.[Status]"]),
        (SortFields.ApplicationUsers.LastLoginAt, ["u.[LastLoginUtc]"]),
        (SortFields.ApplicationUsers.CreatedAt, ["u.[CreatedAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ApplicationUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid applicationId,
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
            ApplicationUserSortColumns, sortBy, sortDirection, "u.[Email]", "u.[Id]");

        // A user belongs to the application when they hold an active app-scoped
        // role assignment, directly (UserRoles) or through an organization
        // (OrganizationUserRoles) — same signals as HasActiveUserAssignmentsAsync.
        const string filter = @"
            u.[IsDeleted] = 0
              AND (@SearchPattern IS NULL OR
                   u.[Email] LIKE @SearchPattern OR
                   u.[FirstName] LIKE @SearchPattern OR
                   u.[LastName] LIKE @SearchPattern)
              AND (
                  EXISTS (
                      SELECT 1 FROM [dbo].[UserRoles] ur
                      WHERE ur.[UserId] = u.[Id]
                        AND ur.[ApplicationId] = @ApplicationId
                        AND ur.[IsActive] = 1
                        AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE()))
                  OR EXISTS (
                      SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                      WHERE our.[UserId] = u.[Id]
                        AND our.[ApplicationId] = @ApplicationId
                        AND our.[IsActive] = 1
                        AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))
              )";

        var sql = $@"
            SELECT COUNT(1) FROM [dbo].[Users] u
            WHERE {filter};

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
                rn.[RoleNames]
            FROM [dbo].[Users] u
            OUTER APPLY (
                SELECT STRING_AGG(x.[Name], ', ') WITHIN GROUP (ORDER BY x.[Name]) AS [RoleNames]
                FROM (
                    SELECT r.[Name]
                    FROM [dbo].[UserRoles] ur
                    INNER JOIN [dbo].[Roles] r ON ur.[RoleId] = r.[Id]
                    WHERE ur.[UserId] = u.[Id]
                      AND ur.[ApplicationId] = @ApplicationId
                      AND ur.[IsActive] = 1
                      AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
                    UNION
                    SELECT r.[Name]
                    FROM [dbo].[OrganizationUserRoles] our
                    INNER JOIN [dbo].[Roles] r ON our.[RoleId] = r.[Id]
                    WHERE our.[UserId] = u.[Id]
                      AND our.[ApplicationId] = @ApplicationId
                      AND our.[IsActive] = 1
                      AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
                ) x
            ) rn
            WHERE {filter}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            ApplicationId = applicationId,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var users = (await multi.ReadAsync<ApplicationUserRow>()).ToList();

        return (users, totalCount);
    }

    private static readonly IReadOnlyDictionary<string, string[]> ApplicationOrganizationSortColumns = SortSql.Map(
        (SortFields.ApplicationOrganizations.Name, ["o.[Name]"]),
        (SortFields.ApplicationOrganizations.Code, ["o.[Code]"]),
        (SortFields.ApplicationOrganizations.EnabledAt, ["oa.[EnabledAt]"]),
        (SortFields.ApplicationOrganizations.ExpiresAt, ["oa.[ExpiresAt]"]),
        (SortFields.ApplicationOrganizations.IsActive, ["oa.[IsActive]"]),
        (SortFields.ApplicationOrganizations.OrganizationIsActive, ["o.[IsActive]"]),
        (SortFields.ApplicationOrganizations.MemberCount, ["[MemberCount]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ApplicationOrganizationRow> Organizations, int TotalCount)> GetOrganizationsPagedAsync(
        Guid applicationId,
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
            ApplicationOrganizationSortColumns, sortBy, sortDirection, "o.[Name]", "o.[Id]");

        // Inactive enablement links are included so admins can see disabled tenants.
        var sql = $@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationApplications] oa
            INNER JOIN [dbo].[Organizations] o ON oa.[OrganizationId] = o.[Id]
            WHERE oa.[ApplicationId] = @ApplicationId
              AND (@SearchPattern IS NULL OR
                   o.[Name] LIKE @SearchPattern OR
                   o.[Code] LIKE @SearchPattern);

            SELECT
                o.[Id] AS [OrganizationId],
                o.[Code],
                o.[Name],
                o.[LogoUrl],
                o.[IsActive] AS [OrganizationIsActive],
                oa.[IsActive] AS [LinkIsActive],
                oa.[EnabledAt],
                oa.[ExpiresAt],
                (SELECT COUNT(1) FROM [dbo].[OrganizationUsers] ou
                 WHERE ou.[OrganizationId] = o.[Id] AND ou.[IsActive] = 1) AS [MemberCount]
            FROM [dbo].[OrganizationApplications] oa
            INNER JOIN [dbo].[Organizations] o ON oa.[OrganizationId] = o.[Id]
            WHERE oa.[ApplicationId] = @ApplicationId
              AND (@SearchPattern IS NULL OR
                   o.[Name] LIKE @SearchPattern OR
                   o.[Code] LIKE @SearchPattern)
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            ApplicationId = applicationId,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var organizations = (await multi.ReadAsync<ApplicationOrganizationRow>()).ToList();

        return (organizations, totalCount);
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
