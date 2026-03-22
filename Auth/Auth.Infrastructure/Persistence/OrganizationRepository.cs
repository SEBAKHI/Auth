using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the organization repository.
/// Handles organizations, memberships, app subscriptions, and permissions.
/// </summary>
public class OrganizationRepository : IOrganizationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrganizationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    #region Organization CRUD

    /// <inheritdoc />
    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Organizations]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<Organization?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Organizations]
            WHERE [Code] = @Code",
            new { Code = code.ToLowerInvariant() });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Organizations]
            WHERE [Code] = @Code",
            new { Code = code.ToLowerInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Organization>> GetByOwnerAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationDto>(@"
            SELECT
                [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Organizations]
            WHERE [OwnerId] = @OwnerId
            ORDER BY [Name]",
            new { OwnerId = ownerId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Organizations] (
                [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Code, @Name, @Description, @LogoUrl, @Website,
                @ContactEmail, @OwnerId, @IsActive, @IsAutoCreated,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                organization.Id,
                organization.Code,
                organization.Name,
                organization.Description,
                organization.LogoUrl,
                organization.Website,
                ContactEmail = organization.ContactEmail.Value,
                organization.OwnerId,
                organization.IsActive,
                organization.IsAutoCreated,
                organization.CreatedAt,
                organization.CreatedBy,
                organization.ModifiedAt,
                organization.ModifiedBy
            });

        return organization;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Organization organization, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Organizations] SET
                [Name] = @Name,
                [Description] = @Description,
                [LogoUrl] = @LogoUrl,
                [Website] = @Website,
                [ContactEmail] = @ContactEmail,
                [IsActive] = @IsActive,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                organization.Id,
                organization.Name,
                organization.Description,
                organization.LogoUrl,
                organization.Website,
                ContactEmail = organization.ContactEmail.Value,
                organization.IsActive,
                organization.ModifiedAt,
                organization.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Cascade delete is handled by FK constraints
        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[Organizations]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    #endregion

    #region Organization Membership

    /// <inheritdoc />
    public async Task<OrganizationUser?> GetMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationUserDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [RoleId], [IsActive],
                [JoinedAt], [InvitedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUsers]
            WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId",
            new { OrganizationId = organizationId, UserId = userId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Organization>> GetUserOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationDto>(@"
            SELECT
                o.[Id], o.[Code], o.[Name], o.[Description], o.[LogoUrl], o.[Website],
                o.[ContactEmail], o.[OwnerId], o.[IsActive], o.[IsAutoCreated],
                o.[CreatedAt], o.[CreatedBy], o.[ModifiedAt], o.[ModifiedBy]
            FROM [dbo].[Organizations] o
            INNER JOIN [dbo].[OrganizationUsers] ou ON o.[Id] = ou.[OrganizationId]
            WHERE ou.[UserId] = @UserId AND ou.[IsActive] = 1 AND o.[IsActive] = 1
            ORDER BY o.[Name]",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUser>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserDto>(@"
            SELECT
                ou.[Id], ou.[OrganizationId], ou.[UserId], ou.[RoleId], ou.[IsActive],
                ou.[JoinedAt], ou.[InvitedBy], ou.[ExpiresAt],
                ou.[CreatedAt], ou.[CreatedBy], ou.[ModifiedAt], ou.[ModifiedBy]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            WHERE ou.[UserId] = @UserId AND ou.[IsActive] = 1 AND o.[IsActive] = 1",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUser>> GetMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [RoleId], [IsActive],
                [JoinedAt], [InvitedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUsers]
            WHERE [OrganizationId] = @OrganizationId AND [IsActive] = 1
            ORDER BY [JoinedAt]",
            new { OrganizationId = organizationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<OrganizationUser> Members, int TotalCount)> GetMembersPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var offset = (pageNumber - 1) * pageSize;
        var searchPattern = string.IsNullOrEmpty(searchTerm) ? null : $"%{searchTerm}%";

        // Get total count
        var countSql = @"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            WHERE ou.[OrganizationId] = @OrganizationId AND ou.[IsActive] = 1
            AND (@SearchPattern IS NULL OR u.[Email] LIKE @SearchPattern OR u.[FirstName] LIKE @SearchPattern OR u.[LastName] LIKE @SearchPattern)";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
        {
            OrganizationId = organizationId,
            SearchPattern = searchPattern
        });

        // Get paged results
        var sql = @"
            SELECT
                ou.[Id], ou.[OrganizationId], ou.[UserId], ou.[RoleId], ou.[IsActive],
                ou.[JoinedAt], ou.[InvitedBy], ou.[ExpiresAt],
                ou.[CreatedAt], ou.[CreatedBy], ou.[ModifiedAt], ou.[ModifiedBy]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            WHERE ou.[OrganizationId] = @OrganizationId AND ou.[IsActive] = 1
            AND (@SearchPattern IS NULL OR u.[Email] LIKE @SearchPattern OR u.[FirstName] LIKE @SearchPattern OR u.[LastName] LIKE @SearchPattern)
            ORDER BY ou.[JoinedAt]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var dtos = await connection.QueryAsync<OrganizationUserDto>(sql, new
        {
            OrganizationId = organizationId,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var members = dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
        return (members, totalCount);
    }

    /// <inheritdoc />
    public async Task<OrganizationUser> AddMemberAsync(
        OrganizationUser membership,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OrganizationUsers] (
                [Id], [OrganizationId], [UserId], [RoleId], [IsActive],
                [JoinedAt], [InvitedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @OrganizationId, @UserId, @RoleId, @IsActive,
                @JoinedAt, @InvitedBy, @ExpiresAt,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                membership.Id,
                membership.OrganizationId,
                membership.UserId,
                membership.RoleId,
                membership.IsActive,
                membership.JoinedAt,
                membership.InvitedBy,
                membership.ExpiresAt,
                membership.CreatedAt,
                membership.CreatedBy,
                membership.ModifiedAt,
                membership.ModifiedBy
            });

        return membership;
    }

    /// <inheritdoc />
    public async Task UpdateMemberAsync(
        OrganizationUser membership,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationUsers] SET
                [RoleId] = @RoleId,
                [IsActive] = @IsActive,
                [ExpiresAt] = @ExpiresAt,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                membership.Id,
                membership.RoleId,
                membership.IsActive,
                membership.ExpiresAt,
                membership.ModifiedAt,
                membership.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[OrganizationUsers]
            WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId",
            new { OrganizationId = organizationId, UserId = userId });
    }

    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OrganizationUsers]
            WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId AND [IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId });

        return count > 0;
    }

    #endregion

    #region Organization Applications (Subscriptions)

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationApplication>> GetEnabledApplicationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationApplicationDto>(@"
            SELECT
                [Id], [OrganizationId], [ApplicationId], [IsActive],
                [EnabledAt], [EnabledBy], [ExpiresAt], [SubscriptionTier],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationApplications]
            WHERE [OrganizationId] = @OrganizationId AND [IsActive] = 1",
            new { OrganizationId = organizationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrganizationApplication?> GetApplicationSubscriptionAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationApplicationDto>(@"
            SELECT
                [Id], [OrganizationId], [ApplicationId], [IsActive],
                [EnabledAt], [EnabledBy], [ExpiresAt], [SubscriptionTier],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationApplications]
            WHERE [OrganizationId] = @OrganizationId AND [ApplicationId] = @ApplicationId",
            new { OrganizationId = organizationId, ApplicationId = applicationId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<OrganizationApplication> EnableApplicationAsync(
        OrganizationApplication subscription,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OrganizationApplications] (
                [Id], [OrganizationId], [ApplicationId], [IsActive],
                [EnabledAt], [EnabledBy], [ExpiresAt], [SubscriptionTier],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @OrganizationId, @ApplicationId, @IsActive,
                @EnabledAt, @EnabledBy, @ExpiresAt, @SubscriptionTier,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                subscription.Id,
                subscription.OrganizationId,
                subscription.ApplicationId,
                subscription.IsActive,
                subscription.EnabledAt,
                subscription.EnabledBy,
                subscription.ExpiresAt,
                subscription.SubscriptionTier,
                subscription.CreatedAt,
                subscription.CreatedBy,
                subscription.ModifiedAt,
                subscription.ModifiedBy
            });

        return subscription;
    }

    /// <inheritdoc />
    public async Task UpdateApplicationSubscriptionAsync(
        OrganizationApplication subscription,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationApplications] SET
                [IsActive] = @IsActive,
                [ExpiresAt] = @ExpiresAt,
                [SubscriptionTier] = @SubscriptionTier,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                subscription.Id,
                subscription.IsActive,
                subscription.ExpiresAt,
                subscription.SubscriptionTier,
                subscription.ModifiedAt,
                subscription.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DisableApplicationAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationApplications] SET
                [IsActive] = 0,
                [ModifiedAt] = @ModifiedAt
            WHERE [OrganizationId] = @OrganizationId AND [ApplicationId] = @ApplicationId",
            new
            {
                OrganizationId = organizationId,
                ApplicationId = applicationId,
                ModifiedAt = DateTime.UtcNow
            });
    }

    /// <inheritdoc />
    public async Task<bool> IsApplicationEnabledAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OrganizationApplications]
            WHERE [OrganizationId] = @OrganizationId
              AND [ApplicationId] = @ApplicationId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { OrganizationId = organizationId, ApplicationId = applicationId });

        return count > 0;
    }

    #endregion

    #region Organization User Roles (App-level roles within org)

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUserRole>> GetUserAppRolesAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserRoleDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [ApplicationId], [RoleId], [IsActive],
                [AssignedAt], [AssignedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUserRoles]
            WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId AND [IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUserRole>> GetUserAppRolesAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserRoleDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [ApplicationId], [RoleId], [IsActive],
                [AssignedAt], [AssignedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUserRoles]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId, ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrganizationUserRole> AssignAppRoleAsync(
        OrganizationUserRole assignment,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OrganizationUserRoles] (
                [Id], [OrganizationId], [UserId], [ApplicationId], [RoleId], [IsActive],
                [AssignedAt], [AssignedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @OrganizationId, @UserId, @ApplicationId, @RoleId, @IsActive,
                @AssignedAt, @AssignedBy, @ExpiresAt,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                assignment.Id,
                assignment.OrganizationId,
                assignment.UserId,
                assignment.ApplicationId,
                assignment.RoleId,
                assignment.IsActive,
                assignment.AssignedAt,
                assignment.AssignedBy,
                assignment.ExpiresAt,
                assignment.CreatedAt,
                assignment.CreatedBy,
                assignment.ModifiedAt,
                assignment.ModifiedBy
            });

        return assignment;
    }

    /// <inheritdoc />
    public async Task RemoveAppRoleAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[OrganizationUserRoles]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [RoleId] = @RoleId",
            new
            {
                OrganizationId = organizationId,
                UserId = userId,
                ApplicationId = applicationId,
                RoleId = roleId
            });
    }

    /// <inheritdoc />
    public async Task<bool> HasAppRoleAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OrganizationUserRoles]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [RoleId] = @RoleId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new
            {
                OrganizationId = organizationId,
                UserId = userId,
                ApplicationId = applicationId,
                RoleId = roleId
            });

        return count > 0;
    }

    #endregion

    #region Organization User Permissions (Individual grants within org)

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUserPermission>> GetUserPermissionsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserPermissionDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [ApplicationId], [PermissionId], [IsActive],
                [GrantedAt], [GrantedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUserPermissions]
            WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId AND [IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationUserPermission>> GetUserPermissionsAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationUserPermissionDto>(@"
            SELECT
                [Id], [OrganizationId], [UserId], [ApplicationId], [PermissionId], [IsActive],
                [GrantedAt], [GrantedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[OrganizationUserPermissions]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId, ApplicationId = applicationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrganizationUserPermission> GrantPermissionAsync(
        OrganizationUserPermission grant,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OrganizationUserPermissions] (
                [Id], [OrganizationId], [UserId], [ApplicationId], [PermissionId], [IsActive],
                [GrantedAt], [GrantedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @OrganizationId, @UserId, @ApplicationId, @PermissionId, @IsActive,
                @GrantedAt, @GrantedBy, @ExpiresAt,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                grant.Id,
                grant.OrganizationId,
                grant.UserId,
                grant.ApplicationId,
                grant.PermissionId,
                grant.IsActive,
                grant.GrantedAt,
                grant.GrantedBy,
                grant.ExpiresAt,
                grant.CreatedAt,
                grant.CreatedBy,
                grant.ModifiedAt,
                grant.ModifiedBy
            });

        return grant;
    }

    /// <inheritdoc />
    public async Task RevokePermissionAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[OrganizationUserPermissions]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [PermissionId] = @PermissionId",
            new
            {
                OrganizationId = organizationId,
                UserId = userId,
                ApplicationId = applicationId,
                PermissionId = permissionId
            });
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[OrganizationUserPermissions]
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @UserId
              AND [ApplicationId] = @ApplicationId
              AND [PermissionId] = @PermissionId
              AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new
            {
                OrganizationId = organizationId,
                UserId = userId,
                ApplicationId = applicationId,
                PermissionId = permissionId
            });

        return count > 0;
    }

    #endregion

    #region Authorization Helpers

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Get permissions from both app-level roles AND individual grants
        var permissionCodes = await connection.QueryAsync<string>(@"
            -- Permissions from app-level roles (via RolePermissions)
            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUserRoles] our
            INNER JOIN [dbo].[RolePermissions] rp ON our.[RoleId] = rp.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON rp.[PermissionId] = p.[Id]
            WHERE our.[OrganizationId] = @OrganizationId
              AND our.[UserId] = @UserId
              AND our.[ApplicationId] = @ApplicationId
              AND our.[IsActive] = 1
              AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
              AND p.[IsActive] = 1

            UNION

            -- Permissions from individual grants
            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUserPermissions] oup
            INNER JOIN [dbo].[Permissions] p ON oup.[PermissionId] = p.[Id]
            WHERE oup.[OrganizationId] = @OrganizationId
              AND oup.[UserId] = @UserId
              AND oup.[ApplicationId] = @ApplicationId
              AND oup.[IsActive] = 1
              AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE())
              AND p.[IsActive] = 1",
            new { OrganizationId = organizationId, UserId = userId, ApplicationId = applicationId });

        return permissionCodes.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> HasAppAccessAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Check if user has access to app through any org:
        // 1. User is a member of an org
        // 2. That org has the app enabled
        // 3. User has at least one role OR permission for that app in that org
        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa ON o.[Id] = oa.[OrganizationId]
            WHERE ou.[UserId] = @UserId
              AND oa.[ApplicationId] = @ApplicationId
              AND ou.[IsActive] = 1
              AND o.[IsActive] = 1
              AND oa.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (
                  -- Has at least one app-level role
                  EXISTS (
                      SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                      WHERE our.[OrganizationId] = o.[Id]
                        AND our.[UserId] = @UserId
                        AND our.[ApplicationId] = @ApplicationId
                        AND our.[IsActive] = 1
                        AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
                  )
                  OR
                  -- Has at least one individual permission
                  EXISTS (
                      SELECT 1 FROM [dbo].[OrganizationUserPermissions] oup
                      WHERE oup.[OrganizationId] = o.[Id]
                        AND oup.[UserId] = @UserId
                        AND oup.[ApplicationId] = @ApplicationId
                        AND oup.[IsActive] = 1
                        AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE())
                  )
              )",
            new { UserId = userId, ApplicationId = applicationId });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionInAnyOrgAsync(
        Guid userId,
        Guid applicationId,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Check if user has the permission through any org (via role or direct grant)
        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa ON o.[Id] = oa.[OrganizationId]
            WHERE ou.[UserId] = @UserId
              AND oa.[ApplicationId] = @ApplicationId
              AND ou.[IsActive] = 1
              AND o.[IsActive] = 1
              AND oa.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (
                  -- Permission from app-level role
                  EXISTS (
                      SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                      INNER JOIN [dbo].[RolePermissions] rp ON our.[RoleId] = rp.[RoleId]
                      INNER JOIN [dbo].[Permissions] p ON rp.[PermissionId] = p.[Id]
                      WHERE our.[OrganizationId] = o.[Id]
                        AND our.[UserId] = @UserId
                        AND our.[ApplicationId] = @ApplicationId
                        AND our.[IsActive] = 1
                        AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
                        AND p.[Code] = @PermissionCode
                        AND p.[IsActive] = 1
                  )
                  OR
                  -- Permission from individual grant
                  EXISTS (
                      SELECT 1 FROM [dbo].[OrganizationUserPermissions] oup
                      INNER JOIN [dbo].[Permissions] p ON oup.[PermissionId] = p.[Id]
                      WHERE oup.[OrganizationId] = o.[Id]
                        AND oup.[UserId] = @UserId
                        AND oup.[ApplicationId] = @ApplicationId
                        AND oup.[IsActive] = 1
                        AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE())
                        AND p.[Code] = @PermissionCode
                        AND p.[IsActive] = 1
                  )
              )",
            new { UserId = userId, ApplicationId = applicationId, PermissionCode = permissionCode });

        return count > 0;
    }

    #endregion

    #region Invitations

    /// <inheritdoc />
    public async Task<OrganizationInvitation?> GetInvitationByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationInvitationDto>(@"
            SELECT
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt]
            FROM [dbo].[OrganizationInvitations]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<OrganizationInvitation?> GetInvitationByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationInvitationDto>(@"
            SELECT
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt]
            FROM [dbo].[OrganizationInvitations]
            WHERE [Token] = @Token",
            new { Token = token });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationInvitation>> GetPendingInvitationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationInvitationDto>(@"
            SELECT
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt]
            FROM [dbo].[OrganizationInvitations]
            WHERE [OrganizationId] = @OrganizationId AND [Status] = 'Pending'
            ORDER BY [CreatedAt] DESC",
            new { OrganizationId = organizationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationInvitation>> GetPendingInvitationsByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<OrganizationInvitationDto>(@"
            SELECT
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt]
            FROM [dbo].[OrganizationInvitations]
            WHERE [Email] = @Email AND [Status] = 'Pending'
            ORDER BY [CreatedAt] DESC",
            new { Email = email.ToLowerInvariant() });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrganizationInvitation> CreateInvitationAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[OrganizationInvitations] (
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt]
            ) VALUES (
                @Id, @OrganizationId, @Email, @RoleId, @Token, @Status,
                @ExpiresAt, @InvitedBy, @AcceptedAt, @AcceptedByUserId, @CreatedAt
            )",
            new
            {
                invitation.Id,
                invitation.OrganizationId,
                invitation.Email,
                invitation.RoleId,
                invitation.Token,
                Status = invitation.Status.ToString(),
                invitation.ExpiresAt,
                invitation.InvitedBy,
                invitation.AcceptedAt,
                invitation.AcceptedByUserId,
                invitation.CreatedAt
            });

        return invitation;
    }

    /// <inheritdoc />
    public async Task UpdateInvitationAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationInvitations] SET
                [Status] = @Status,
                [AcceptedAt] = @AcceptedAt,
                [AcceptedByUserId] = @AcceptedByUserId
            WHERE [Id] = @Id",
            new
            {
                invitation.Id,
                Status = invitation.Status.ToString(),
                invitation.AcceptedAt,
                invitation.AcceptedByUserId
            });
    }

    /// <inheritdoc />
    public async Task DeleteInvitationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            DELETE FROM [dbo].[OrganizationInvitations]
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task MarkExpiredInvitationsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationInvitations]
            SET [Status] = 'Expired'
            WHERE [Status] = 'Pending' AND [ExpiresAt] < GETUTCDATE()");
    }

    #endregion

    #region Internal DTOs

    private record OrganizationDto
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? LogoUrl { get; init; }
        public string? Website { get; init; }
        public string ContactEmail { get; init; } = string.Empty;
        public Guid OwnerId { get; init; }
        public bool IsActive { get; init; }
        public bool IsAutoCreated { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public Organization ToEntity() => new(
            Id,
            Code,
            Name,
            Description,
            LogoUrl,
            Website,
            ContactEmail,
            OwnerId,
            IsActive,
            IsAutoCreated,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record OrganizationUserDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid UserId { get; init; }
        public Guid RoleId { get; init; }
        public bool IsActive { get; init; }
        public DateTime JoinedAt { get; init; }
        public Guid InvitedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public OrganizationUser ToEntity() => new(
            Id,
            OrganizationId,
            UserId,
            RoleId,
            IsActive,
            JoinedAt,
            InvitedBy,
            ExpiresAt,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record OrganizationApplicationDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid ApplicationId { get; init; }
        public bool IsActive { get; init; }
        public DateTime EnabledAt { get; init; }
        public Guid EnabledBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public string? SubscriptionTier { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public OrganizationApplication ToEntity() => new(
            Id,
            OrganizationId,
            ApplicationId,
            IsActive,
            EnabledAt,
            EnabledBy,
            ExpiresAt,
            SubscriptionTier,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record OrganizationUserRoleDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid UserId { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid RoleId { get; init; }
        public bool IsActive { get; init; }
        public DateTime AssignedAt { get; init; }
        public Guid AssignedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public OrganizationUserRole ToEntity() => new(
            Id,
            OrganizationId,
            UserId,
            ApplicationId,
            RoleId,
            IsActive,
            AssignedAt,
            AssignedBy,
            ExpiresAt,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record OrganizationUserPermissionDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid UserId { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid PermissionId { get; init; }
        public bool IsActive { get; init; }
        public DateTime GrantedAt { get; init; }
        public Guid GrantedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

        public OrganizationUserPermission ToEntity() => new(
            Id,
            OrganizationId,
            UserId,
            ApplicationId,
            PermissionId,
            IsActive,
            GrantedAt,
            GrantedBy,
            ExpiresAt,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy);
    }

    private record OrganizationInvitationDto
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public string Email { get; init; } = string.Empty;
        public Guid RoleId { get; init; }
        public string Token { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public Guid InvitedBy { get; init; }
        public DateTime? AcceptedAt { get; init; }
        public Guid? AcceptedByUserId { get; init; }
        public DateTime CreatedAt { get; init; }

        public OrganizationInvitation ToEntity()
        {
            var status = Enum.Parse<InvitationStatus>(Status);
            return new OrganizationInvitation(
                Id,
                OrganizationId,
                Email,
                RoleId,
                Token,
                status,
                ExpiresAt,
                InvitedBy,
                AcceptedAt,
                AcceptedByUserId,
                CreatedAt);
        }
    }

    #endregion
}
