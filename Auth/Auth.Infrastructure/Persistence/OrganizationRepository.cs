using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Domain.ValueObjects;
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
    public async Task<bool> TransferOwnershipAsync(
        Guid organizationId,
        Guid previousOwnerId,
        Guid newOwnerId,
        Guid ownerRoleId,
        Guid demotedRoleId,
        Guid modifiedBy,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Conditional on the expected owner so a concurrent transfer loses
        // cleanly instead of producing a second owner.
        var ownerRows = await connection.ExecuteAsync(@"
            UPDATE [dbo].[Organizations] SET
                [OwnerId] = @NewOwnerId,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @OrganizationId
              AND [OwnerId] = @PreviousOwnerId",
            new { OrganizationId = organizationId, NewOwnerId = newOwnerId, PreviousOwnerId = previousOwnerId, ModifiedBy = modifiedBy },
            transaction);

        if (ownerRows == 0)
        {
            transaction.Rollback();
            return false;
        }

        // Promote the new owner's membership to the owner role.
        var promotedRows = await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationUsers] SET
                [RoleId] = @OwnerRoleId,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @NewOwnerId",
            new { OrganizationId = organizationId, NewOwnerId = newOwnerId, OwnerRoleId = ownerRoleId, ModifiedBy = modifiedBy },
            transaction);

        if (promotedRows == 0)
        {
            transaction.Rollback();
            return false;
        }

        // Demote the previous owner's membership. Zero rows is tolerated: an
        // owner without a membership row is legacy data, not a failed transfer.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationUsers] SET
                [RoleId] = @DemotedRoleId,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [OrganizationId] = @OrganizationId
              AND [UserId] = @PreviousOwnerId",
            new { OrganizationId = organizationId, PreviousOwnerId = previousOwnerId, DemotedRoleId = demotedRoleId, ModifiedBy = modifiedBy },
            transaction);

        transaction.Commit();
        return true;
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

    private static readonly IReadOnlyDictionary<string, string[]> PagedSortColumns = SortSql.Map(
        (SortFields.Organizations.Name, ["[Name]"]),
        (SortFields.Organizations.Code, ["[Code]"]),
        (SortFields.Organizations.ContactEmail, ["[ContactEmail]"]),
        (SortFields.Organizations.IsActive, ["[IsActive]"]),
        (SortFields.Organizations.MemberCount,
            ["(SELECT COUNT(1) FROM [dbo].[OrganizationUsers] WHERE [OrganizationId] = [Organizations].[Id] AND [IsActive] = 1)"]),
        (SortFields.Organizations.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Organizations.ModifiedAt, ["[ModifiedAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Organization> Organizations, int TotalCount)> GetPagedAsync(
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
            PagedSortColumns, sortBy, sortDirection, "[Name] ASC", "[Id]");

        var sql = $@"
            SELECT COUNT(1) FROM [dbo].[Organizations]
            WHERE (@SearchPattern IS NULL OR
                   [Name] LIKE @SearchPattern OR
                   [Code] LIKE @SearchPattern OR
                   [ContactEmail] LIKE @SearchPattern);

            SELECT
                [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Organizations]
            WHERE (@SearchPattern IS NULL OR
                   [Name] LIKE @SearchPattern OR
                   [Code] LIKE @SearchPattern OR
                   [ContactEmail] LIKE @SearchPattern)
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var organizations = (await multi.ReadAsync<OrganizationDto>())
            .Select(dto => dto.ToEntity())
            .ToList();

        return (organizations, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(
        IReadOnlyCollection<Guid> organizationIds,
        CancellationToken cancellationToken)
    {
        if (organizationIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(Guid OrganizationId, int Count)>(@"
            SELECT [OrganizationId], COUNT(1) AS [Count]
            FROM [dbo].[OrganizationUsers]
            WHERE [OrganizationId] IN @Ids AND [IsActive] = 1
            GROUP BY [OrganizationId]",
            new { Ids = organizationIds });

        return rows.ToDictionary(r => r.OrganizationId, r => r.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetEnabledApplicationCountsAsync(
        IReadOnlyCollection<Guid> organizationIds,
        CancellationToken cancellationToken)
    {
        if (organizationIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(Guid OrganizationId, int Count)>(@"
            SELECT [OrganizationId], COUNT(1) AS [Count]
            FROM [dbo].[OrganizationApplications]
            WHERE [OrganizationId] IN @Ids AND [IsActive] = 1
            GROUP BY [OrganizationId]",
            new { Ids = organizationIds });

        return rows.ToDictionary(r => r.OrganizationId, r => r.Count);
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
    public async Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetMembershipPermissionCodesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(Guid OrganizationId, string Code)>(@"
            SELECT DISTINCT ou.[OrganizationId], p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[RolePermissions] rp ON rp.[RoleId] = ou.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1
              AND o.[IsActive] = 1
              AND p.[IsActive] = 1
              -- A membership yields organization authority and nothing else. The role bound to
              -- OrganizationUsers is only checked for not belonging to an application, and that
              -- does not exclude a platform role: binding super-admin as a membership role used
              -- to return its wildcard row here and mint an org_perm claim of {orgId}:* for it.
              -- Filtering on the code rather than on the write path also neutralises any row
              -- already bound that way, because authority is re-derived here on every request.
              AND p.[Code] LIKE 'org:%'
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())",
            new { UserId = userId });

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetMembershipPermissionCodesForApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The platform-wide query above, narrowed to organizations that have
        // this application enabled. An application-scoped token has no business
        // carrying the user's memberships in organizations that never heard of
        // it — the claim's only consumer is the platform API, which never
        // receives an application-audience token anyway.
        var rows = await connection.QueryAsync<(Guid OrganizationId, string Code)>(@"
            SELECT DISTINCT ou.[OrganizationId], p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = ou.[OrganizationId]
               AND oa.[ApplicationId] = @ApplicationId
               AND oa.[IsActive] = 1
               AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
            INNER JOIN [dbo].[RolePermissions] rp ON rp.[RoleId] = ou.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1
              AND o.[IsActive] = 1
              AND p.[IsActive] = 1
              -- A membership yields organization authority and nothing else. The role bound to
              -- OrganizationUsers is only checked for not belonging to an application, and that
              -- does not exclude a platform role: binding super-admin as a membership role used
              -- to return its wildcard row here and mint an org_perm claim of {orgId}:* for it.
              -- Filtering on the code rather than on the write path also neutralises any row
              -- already bound that way, because authority is re-derived here on every request.
              AND p.[Code] LIKE 'org:%'
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, ApplicationId = applicationId });

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetMembershipPermissionCodesAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var codes = await connection.QueryAsync<string>(@"
            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[RolePermissions] rp ON rp.[RoleId] = ou.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
            WHERE ou.[OrganizationId] = @OrganizationId
              AND ou.[UserId] = @UserId
              AND ou.[IsActive] = 1
              AND o.[IsActive] = 1
              AND p.[IsActive] = 1
              -- A membership yields organization authority and nothing else. The role bound to
              -- OrganizationUsers is only checked for not belonging to an application, and that
              -- does not exclude a platform role: binding super-admin as a membership role used
              -- to return its wildcard row here and mint an org_perm claim of {orgId}:* for it.
              -- Filtering on the code rather than on the write path also neutralises any row
              -- already bound that way, because authority is re-derived here on every request.
              AND p.[Code] LIKE 'org:%'
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())",
            new { OrganizationId = organizationId, UserId = userId });

        return codes.ToList();
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

    // Name/email sort on the joined Users row, role on the joined Roles row,
    // inviter on a second Users join (all LEFT and 1:1 by primary key).
    private static readonly IReadOnlyDictionary<string, string[]> MemberSortColumns = SortSql.Map(
        (SortFields.OrganizationMembers.Name, ["u.[FirstName]", "u.[LastName]"]),
        (SortFields.OrganizationMembers.FullName, ["u.[FullName]"]),
        (SortFields.OrganizationMembers.FirstName, ["u.[FirstName]"]),
        (SortFields.OrganizationMembers.LastName, ["u.[LastName]"]),
        (SortFields.OrganizationMembers.Email, ["u.[Email]"]),
        (SortFields.OrganizationMembers.RoleName, ["r.[Name]"]),
        (SortFields.OrganizationMembers.RoleCode, ["r.[Code]"]),
        (SortFields.OrganizationMembers.IsActive, ["ou.[IsActive]"]),
        (SortFields.OrganizationMembers.JoinedAt, ["ou.[JoinedAt]"]),
        (SortFields.OrganizationMembers.InvitedByName, ["inv.[FullName]"]),
        (SortFields.OrganizationMembers.ExpiresAt, ["ou.[ExpiresAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<OrganizationUser> Members, int TotalCount)> GetMembersPagedAsync(
        Guid organizationId,
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

        // Get total count
        var countSql = $@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            WHERE ou.[OrganizationId] = @OrganizationId AND ou.[IsActive] = 1
            AND {UserSearchSql.Matches("u")}";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, new
        {
            OrganizationId = organizationId,
            SearchPattern = searchPattern
        });

        // Get paged results
        var orderBy = SortSql.OrderBy(
            MemberSortColumns, sortBy, sortDirection, "ou.[JoinedAt]", "ou.[Id]");
        var sql = $@"
            SELECT
                ou.[Id], ou.[OrganizationId], ou.[UserId], ou.[RoleId], ou.[IsActive],
                ou.[JoinedAt], ou.[InvitedBy], ou.[ExpiresAt],
                ou.[CreatedAt], ou.[CreatedBy], ou.[ModifiedAt], ou.[ModifiedBy]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            LEFT JOIN [dbo].[Roles] r ON ou.[RoleId] = r.[Id]
            LEFT JOIN [dbo].[Users] inv ON ou.[InvitedBy] = inv.[Id]
            WHERE ou.[OrganizationId] = @OrganizationId AND ou.[IsActive] = 1
            AND {UserSearchSql.Matches("u")}
            ORDER BY {orderBy}
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

    /// <summary>
    /// The rows an application's directory may list (OI-194): an active membership, not expired
    /// at the caller's <c>@Now</c>, of an account that is not deleted. One WHERE for the count and
    /// the page, so the total never counts a row the page could not show.
    /// </summary>
    internal const string ActiveMemberWhereSql = @"ou.[OrganizationId] = @OrganizationId
              AND ou.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > @Now)
              AND u.[IsDeleted] = 0";

    /// <inheritdoc />
    public Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> GetActiveMembersPagedAsync(
        Guid organizationId,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        GetDirectoryMembersPagedAsync(organizationId, applicationId: null, now, pageNumber, pageSize, cancellationToken);

    /// <inheritdoc />
    public Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> GetActiveMembersHoldingApplicationRolePagedAsync(
        Guid organizationId,
        Guid applicationId,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        GetDirectoryMembersPagedAsync(organizationId, applicationId, now, pageNumber, pageSize, cancellationToken);

    /// <summary>
    /// The directory's members query (OI-194): with <paramref name="applicationId"/>, narrowed to
    /// the members who hold one of that application's roles by <see cref="MemberApplicationRoleRuleSql"/>
    /// (OI-202). The narrowing is part of the one WHERE the count and the page share.
    /// </summary>
    private async Task<(IReadOnlyList<ApplicationDirectoryMemberRow> Members, int TotalCount)> GetDirectoryMembersPagedAsync(
        Guid organizationId,
        Guid? applicationId,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters(new
        {
            OrganizationId = organizationId,
            // long, then checked: the validator bounds the page number, and this keeps an
            // unchecked caller from wrapping the offset round to a negative number.
            Offset = checked((int)((long)(pageNumber - 1) * pageSize)),
            PageSize = pageSize
        });
        // DATETIME2, as the column is (the same reason as the invitation list's @Now).
        parameters.Add("Now", now, System.Data.DbType.DateTime2);

        var where = ActiveMemberWhereSql;
        if (applicationId is not null)
        {
            parameters.Add("ApplicationId", applicationId.Value);
            where += HoldsApplicationRoleWhereSql;
        }

        var totalCount = await connection.ExecuteScalarAsync<int>($@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            WHERE {where}",
            parameters);

        var rows = await connection.QueryAsync<ApplicationDirectoryMemberRow>($@"
            SELECT
                ou.[Id] AS [MembershipId], ou.[UserId], u.[FirstName], u.[LastName], u.[Email],
                u.[ProfileImageUrl], r.[Code] AS [MembershipRoleCode], ou.[JoinedAt]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Users] u ON ou.[UserId] = u.[Id]
            INNER JOIN [dbo].[Roles] r ON ou.[RoleId] = r.[Id]
            WHERE {where}
            ORDER BY ou.[JoinedAt], ou.[Id]
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters);

        return (rows.ToList().AsReadOnly(), totalCount);
    }

    /// <summary>
    /// The application-role rule of the directory (OI-194): an assignment in the organization
    /// that names the application, active and not expired at the caller's <c>@Now</c>, of an
    /// active role of the same application, for the users <paramref name="userPredicate"/> names.
    /// One rule for the roles a page reports and the members a machine read lists (OI-202).
    /// </summary>
    private static string MemberApplicationRoleRuleSql(string userPredicate) => $@"our.[OrganizationId] = @OrganizationId
              AND our.[ApplicationId] = @ApplicationId
              AND r.[ApplicationId] = @ApplicationId
              AND r.[IsActive] = 1
              AND {userPredicate}
              AND our.[IsActive] = 1
              AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > @Now)";

    /// <summary>The application roles of a page's members (OI-194).</summary>
    internal static readonly string MemberApplicationRoleWhereSql = MemberApplicationRoleRuleSql("our.[UserId] IN @UserIds");

    /// <summary>
    /// The members query's narrowing for a machine read (OI-202): the account is active
    /// (owner decision D-93-2: a deactivated, locked or unverified account is not one the
    /// application serves), and the member holds at least one role by
    /// <see cref="MemberApplicationRoleRuleSql"/> (D-202-3).
    /// </summary>
    internal static readonly string HoldsApplicationRoleWhereSql = $@"
              AND u.[Status] = {(int)UserStatus.Active}
              AND EXISTS (
                  SELECT 1
                  FROM [dbo].[OrganizationUserRoles] our
                  INNER JOIN [dbo].[Roles] r ON our.[RoleId] = r.[Id]
                  WHERE {MemberApplicationRoleRuleSql("our.[UserId] = ou.[UserId]")})";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemberApplicationRoleRow>> GetMemberApplicationRolesAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyCollection<Guid> userIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters(new
        {
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            UserIds = userIds.Distinct().ToArray()
        });
        parameters.Add("Now", now, System.Data.DbType.DateTime2);

        // The assignment names the application, and so does the role: both are tested, so a
        // row bound to a role of another application can never be reported as this one's. A
        // deactivated role is not held either, as the token's roles claim already drops it.
        var rows = await connection.QueryAsync<MemberApplicationRoleRow>($@"
            SELECT our.[UserId], r.[Code] AS [RoleCode]
            FROM [dbo].[OrganizationUserRoles] our
            INNER JOIN [dbo].[Roles] r ON our.[RoleId] = r.[Id]
            WHERE {MemberApplicationRoleWhereSql}
            ORDER BY our.[UserId], r.[Code]",
            parameters);

        return rows.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrganizationUser> AddMemberAsync(
        OrganizationUser membership,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(InsertMemberSql, MemberParameters(membership));

        return membership;
    }

    private const string InsertMemberSql = @"
            INSERT INTO [dbo].[OrganizationUsers] (
                [Id], [OrganizationId], [UserId], [RoleId], [IsActive],
                [JoinedAt], [InvitedBy], [ExpiresAt],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @OrganizationId, @UserId, @RoleId, @IsActive,
                @JoinedAt, @InvitedBy, @ExpiresAt,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )";

    private static object MemberParameters(OrganizationUser membership) => new
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
    };

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
    /// <remarks>
    /// The grant tables have no foreign key to OrganizationUsers (their key is the
    /// (OrganizationId, UserId) pair, not the membership's Id), so deleting the membership
    /// alone left the person's application roles and permissions behind: still read into
    /// tokens, and restored on a later rejoin. One batch deletes the grants, then the
    /// membership, under XACT_ABORT: all of it or none of it.
    ///
    /// Lock order: Organizations (as TransferOwnershipAsync, which updates it before
    /// OrganizationUsers), then OrganizationUsers, then OrganizationUserRoles (as
    /// AssignAppRoleAsync and AcceptInvitationAsync), then OrganizationUserPermissions.
    /// One writer takes them in another order: the user hard purge
    /// (UserRepository.HardDeleteAsync) deletes the grants before the membership,
    /// so a purge racing a removal of the same person can end in a deadlock victim (1205),
    /// which changes nothing. The owner check holds the organization row, so an
    /// ownership transfer cannot slip between the check and the delete; the membership
    /// row is held before the grants, so an assignment either commits first (and its row
    /// is deleted here) or waits and then finds no membership.
    /// </remarks>
    public async Task<OrganizationMemberRemoval> RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        using var results = await connection.QueryMultipleAsync(new CommandDefinition(@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            BEGIN TRY
                DECLARE @Removable BIT = 0;
                DECLARE @MembershipRemoved BIT = 0;

                IF EXISTS (
                    SELECT 1 FROM [dbo].[Organizations] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [Id] = @OrganizationId AND [OwnerId] <> @UserId)
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM [dbo].[OrganizationUsers] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId)
                        SET @Removable = 1;
                END;

                DELETE our
                OUTPUT deleted.[ApplicationId], deleted.[RoleId], r.[Code] AS [RoleCode], r.[Name] AS [RoleName]
                FROM [dbo].[OrganizationUserRoles] our
                LEFT JOIN [dbo].[Roles] r ON r.[Id] = our.[RoleId]
                WHERE our.[OrganizationId] = @OrganizationId AND our.[UserId] = @UserId
                  AND @Removable = 1;

                DELETE oup
                OUTPUT deleted.[ApplicationId], deleted.[PermissionId], p.[Code] AS [PermissionCode]
                FROM [dbo].[OrganizationUserPermissions] oup
                LEFT JOIN [dbo].[Permissions] p ON p.[Id] = oup.[PermissionId]
                WHERE oup.[OrganizationId] = @OrganizationId AND oup.[UserId] = @UserId
                  AND @Removable = 1;

                DELETE ou
                FROM [dbo].[OrganizationUsers] ou
                WHERE ou.[OrganizationId] = @OrganizationId AND ou.[UserId] = @UserId
                  AND @Removable = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM [dbo].[Organizations] o
                      WHERE o.[Id] = ou.[OrganizationId] AND o.[OwnerId] = ou.[UserId]);

                SET @MembershipRemoved = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

                COMMIT TRANSACTION;
                SELECT @MembershipRemoved;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;",
            new { OrganizationId = organizationId, UserId = userId },
            cancellationToken: cancellationToken));

        var applicationRoles = (await results.ReadAsync<RemovedOrganizationApplicationRole>()).ToList();
        var permissions = (await results.ReadAsync<RemovedOrganizationPermission>()).ToList();
        var membershipRemoved = await results.ReadSingleAsync<bool>();

        return new OrganizationMemberRemoval(membershipRemoved, applicationRoles, permissions);
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
    public async Task<IReadOnlyDictionary<Guid, int>> GetAssignedUserCountsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(Guid ApplicationId, int AssignedUserCount)>(@"
            SELECT [ApplicationId], COUNT(DISTINCT [UserId]) AS [AssignedUserCount]
            FROM (
                SELECT [ApplicationId], [UserId]
                FROM [dbo].[OrganizationUserRoles]
                WHERE [OrganizationId] = @OrganizationId
                  AND [IsActive] = 1
                  AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())
                UNION
                SELECT [ApplicationId], [UserId]
                FROM [dbo].[OrganizationUserPermissions]
                WHERE [OrganizationId] = @OrganizationId
                  AND [IsActive] = 1
                  AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())
            ) AS assignments
            GROUP BY [ApplicationId]",
            new { OrganizationId = organizationId });

        return rows.ToDictionary(row => row.ApplicationId, row => row.AssignedUserCount);
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

    /// <summary>
    /// The ONE definition of "an organization application role row counts": joined to an
    /// active, unexpired membership of the same person in the same organization, an active
    /// organization, and that organization's active, unexpired subscription to the row's
    /// application. Without it a removed member, a member of a deactivated organization,
    /// or one whose subscription was switched off kept the role in the application's tokens.
    /// Joined over an <c>[dbo].[OrganizationUserRoles] our</c> row by the token read
    /// (RoleRepository.GetUserRolesForApplicationAsync) and the user page's
    /// cross-organization read, which must list exactly what the token carries. The same
    /// predicates as the role branch of GetEffectivePermissionPairsForApplicationAsync.
    /// A lapsed membership keeps its rows, inert until it is extended or reactivated.
    /// </summary>
    internal const string LiveOrganizationRoleJoinsSql = @"
            INNER JOIN [dbo].[OrganizationUsers] ou
                ON ou.[OrganizationId] = our.[OrganizationId]
               AND ou.[UserId] = our.[UserId]
               AND ou.[IsActive] = 1
               AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
            INNER JOIN [dbo].[Organizations] o
                ON o.[Id] = our.[OrganizationId]
               AND o.[IsActive] = 1
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = our.[OrganizationId]
               AND oa.[ApplicationId] = our.[ApplicationId]
               AND oa.[IsActive] = 1
               AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Auth.Domain.ReadModels.Access.UserOrganizationRoleRow>> GetUserApplicationRolesAcrossOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The organization branch of RoleRepository.GetUserRolesForApplicationAsync,
        // for every organization and application at once: what the user's
        // application tokens carry through an organization. A deleted application is
        // kept and flagged, so the row is not mistaken for something else.
        var rows = await connection.QueryAsync<Auth.Domain.ReadModels.Access.UserOrganizationRoleRow>(new CommandDefinition(@"
            SELECT
                o.[Id] AS [OrganizationId], o.[Code] AS [OrganizationCode], o.[Name] AS [OrganizationName],
                a.[Id] AS [ApplicationId], a.[Code] AS [ApplicationCode], a.[Name] AS [ApplicationName],
                a.[IsDeleted] AS [ApplicationIsDeleted],
                r.[Id] AS [RoleId], r.[Code] AS [RoleCode], r.[Name] AS [RoleName],
                our.[ExpiresAt]
            FROM [dbo].[OrganizationUserRoles] our
            INNER JOIN [dbo].[Roles] r ON r.[Id] = our.[RoleId]" + LiveOrganizationRoleJoinsSql + @"
            INNER JOIN [dbo].[Applications] a ON a.[Id] = our.[ApplicationId]
            WHERE our.[UserId] = @UserId
              AND our.[IsActive] = 1
              AND r.[IsActive] = 1
              AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())
            ORDER BY o.[Name], a.[Name], r.[Name]",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// UQ_OrganizationUserRoles spans (OrganizationId, UserId, ApplicationId, RoleId)
    /// whatever the row's state, so a plain INSERT of a role whose earlier assignment
    /// expired or was deactivated failed on the key (500). The twin of
    /// RoleRepository.AssignToUserAsync: a serializable update-or-insert restores the
    /// exact row with the new terms, or inserts it, and protects the key from a
    /// concurrent insert.
    ///
    /// The membership is checked inside the same statement, under UPDLOCK and HOLDLOCK
    /// (OI-231): a removal that commits between the handler's check and this write would
    /// otherwise leave a role row with no membership behind it. Lock order: OrganizationUsers,
    /// then OrganizationUserRoles, as RemoveMemberAsync takes them.
    /// </remarks>
    public async Task<Guid?> AssignAppRoleAsync(
        OrganizationUserRole assignment,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(@"
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            BEGIN TRY
                DECLARE @RowId UNIQUEIDENTIFIER;

                IF EXISTS (
                    SELECT 1 FROM [dbo].[OrganizationUsers] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId)
                BEGIN
                    " + UpsertAppRoleSql + @"
                END;

                COMMIT TRANSACTION;
                SELECT @RowId;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;",
            AppRoleParameters(assignment),
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The ONE writer of an organization application role on its unique key, shared by
    /// assignment and invitation acceptance (OI-231, deviation 245). UQ_OrganizationUserRoles
    /// spans (OrganizationId, UserId, ApplicationId, RoleId) whatever the row's state, so a
    /// plain INSERT over an expired, deactivated or leftover row failed with 2627 (500): the
    /// existing row is restored with the new terms instead, or a row is inserted. UPDLOCK and
    /// HOLDLOCK hold the key range to the end of the caller's transaction, so the body needs
    /// no transaction of its own and runs inside either. The caller declares
    /// <c>@RowId UNIQUEIDENTIFIER</c>, which answers the row that holds the assignment.
    /// The only other INSERT into the table is ProvisionForApplicationAsync's, whose
    /// organization is created in the same transaction (GrantWritersGuardTests).
    /// </summary>
    private const string UpsertAppRoleSql = @"
                UPDATE [dbo].[OrganizationUserRoles] WITH (UPDLOCK, HOLDLOCK)
                SET @RowId = [Id],
                    [IsActive] = @IsActive,
                    [AssignedAt] = @AssignedAt,
                    [AssignedBy] = @AssignedBy,
                    [ExpiresAt] = @ExpiresAt,
                    [ModifiedAt] = @AssignedAt,
                    [ModifiedBy] = @AssignedBy
                WHERE [OrganizationId] = @OrganizationId
                  AND [UserId] = @UserId
                  AND [ApplicationId] = @ApplicationId
                  AND [RoleId] = @RoleId;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO [dbo].[OrganizationUserRoles] (
                        [Id], [OrganizationId], [UserId], [ApplicationId], [RoleId], [IsActive],
                        [AssignedAt], [AssignedBy], [ExpiresAt],
                        [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
                    ) VALUES (
                        @Id, @OrganizationId, @UserId, @ApplicationId, @RoleId, @IsActive,
                        @AssignedAt, @AssignedBy, @ExpiresAt,
                        @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
                    );
                    SET @RowId = @Id;
                END;";

    private static object AppRoleParameters(OrganizationUserRole assignment) => new
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
    };

    /// <inheritdoc />
    /// <remarks>
    /// The row count is the answer, as in the user-role removal: an existence check
    /// keyed on validity refused an expired row the list still showed (404), and two
    /// concurrent removals both reported success. "> 0" rather than "== 1", so a
    /// database missing the unique key still reports the removal it made.
    /// </remarks>
    public async Task<bool> RemoveAppRoleAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var removed = await connection.ExecuteAsync(@"
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

        return removed > 0;
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

        // Get permissions from both app-level roles AND individual grants, through an
        // active, unexpired membership in this organization only (OI-231): a grant row
        // that outlived its membership is no authority. The organization's own state and
        // its subscription are checked by the callers before they ask.
        var permissionCodes = await connection.QueryAsync<string>(@"
            -- Permissions from app-level roles (via RolePermissions)
            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUserRoles] our
            INNER JOIN [dbo].[OrganizationUsers] ou
                ON ou.[OrganizationId] = our.[OrganizationId]
               AND ou.[UserId] = our.[UserId]
               AND ou.[IsActive] = 1
               AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
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
            INNER JOIN [dbo].[OrganizationUsers] ou
                ON ou.[OrganizationId] = oup.[OrganizationId]
               AND ou.[UserId] = oup.[UserId]
               AND ou.[IsActive] = 1
               AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
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
    public async Task<IReadOnlyList<string>> GetEffectivePermissionCodesForApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // GetEffectivePermissionCodesAsync's two branches, with the single
        // organization replaced by "every organization the user belongs to that
        // has this application enabled". One round trip: the caller is the token
        // mint, where walking memberships one by one cost 1 + 2N queries per
        // sign-in.
        var permissionCodes = await connection.QueryAsync<string>(@"
            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[OrganizationUserRoles] our
                ON our.[OrganizationId] = o.[Id]
               AND our.[UserId] = @UserId
               AND our.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[RolePermissions] rp ON our.[RoleId] = rp.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON rp.[PermissionId] = p.[Id]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1 AND o.[IsActive] = 1 AND oa.[IsActive] = 1
              AND our.[IsActive] = 1 AND p.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())

            UNION

            SELECT DISTINCT p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[OrganizationUserPermissions] oup
                ON oup.[OrganizationId] = o.[Id]
               AND oup.[UserId] = @UserId
               AND oup.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[Permissions] p ON oup.[PermissionId] = p.[Id]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1 AND o.[IsActive] = 1 AND oa.[IsActive] = 1
              AND oup.[IsActive] = 1 AND p.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, ApplicationId = applicationId });

        return permissionCodes.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetEffectivePermissionPairsForApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The same two branches as the overload above, carrying the organization
        // each code was granted in. That column is the entire point: without it a
        // user who is a member of two organizations that both enable this
        // application receives one flat set, and a relying party has no way to
        // tell which grant belongs to which organization - so a permission held
        // in one is spent on the other's data. The codes are NOT filtered to
        // 'org:%' here, unlike the membership queries: these are delegated
        // business permissions, and their whole purpose is to name application
        // capabilities.
        var rows = await connection.QueryAsync<(Guid OrganizationId, string Code)>(@"
            SELECT DISTINCT o.[Id] AS [OrganizationId], p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[OrganizationUserRoles] our
                ON our.[OrganizationId] = o.[Id]
               AND our.[UserId] = @UserId
               AND our.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[RolePermissions] rp ON our.[RoleId] = rp.[RoleId]
            INNER JOIN [dbo].[Permissions] p ON rp.[PermissionId] = p.[Id]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1 AND o.[IsActive] = 1 AND oa.[IsActive] = 1
              AND our.[IsActive] = 1 AND p.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE())

            UNION

            SELECT DISTINCT o.[Id] AS [OrganizationId], p.[Code]
            FROM [dbo].[OrganizationUsers] ou
            INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
            INNER JOIN [dbo].[OrganizationApplications] oa
                ON oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[OrganizationUserPermissions] oup
                ON oup.[OrganizationId] = o.[Id]
               AND oup.[UserId] = @UserId
               AND oup.[ApplicationId] = @ApplicationId
            INNER JOIN [dbo].[Permissions] p ON oup.[PermissionId] = p.[Id]
            WHERE ou.[UserId] = @UserId
              AND ou.[IsActive] = 1 AND o.[IsActive] = 1 AND oa.[IsActive] = 1
              AND oup.[IsActive] = 1 AND p.[IsActive] = 1
              AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
              AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
              AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, ApplicationId = applicationId });

        return rows.ToList();
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
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt],
                [ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]
            FROM [dbo].[OrganizationInvitations]
            WHERE [Id] = @Id",
            new { Id = id });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<OrganizationInvitation?> GetInvitationByTokenHashAsync(
        CredentialHashCandidates candidates,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The [Token] column holds the HMAC fingerprint, not the token. Lookup is by
        // fingerprint alone, exactly as refresh tokens and password-reset tokens are:
        // the value carries enough entropy to identify the row by itself, and the
        // plaintext never has to exist here to find it. Every key it may be stored
        // under is tried (S11).
        var dto = await connection.QueryFirstOrDefaultAsync<OrganizationInvitationDto>(@"
            SELECT
                [Id], [OrganizationId], [Email], [RoleId], [Token], [Status],
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt],
                [ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]
            FROM [dbo].[OrganizationInvitations]
            WHERE [Token] IN @Hashes",
            new { Hashes = candidates.ValuesFor(CredentialPurpose.Invitation) });

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
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt],
                [ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]
            FROM [dbo].[OrganizationInvitations]
            WHERE [OrganizationId] = @OrganizationId AND [Status] = 'Pending'
            ORDER BY [CreatedAt] DESC",
            new { OrganizationId = organizationId });

        return dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly();
    }

    /// <summary>
    /// <see cref="OrganizationInvitation.EffectiveStatus"/> in SQL: a stored-pending row whose
    /// expiry has been reached at <c>@Now</c> reads as Expired. <c>@Now</c> is the caller's
    /// clock, never <c>GETUTCDATE()</c>, so the filter, the sort and the DTO built from the
    /// same rows cannot disagree about a row on the boundary.
    /// </summary>
    internal const string InvitationEffectiveStatusSql =
        "CASE WHEN i.[Status] = 'Pending' AND i.[ExpiresAt] <= @Now THEN 'Expired' ELSE i.[Status] END";

    // Role on the joined Roles row, inviter on the joined Users row (both LEFT and 1:1 by
    // primary key, as in the members list); status and isExpired on the effective status.
    internal static readonly IReadOnlyDictionary<string, string[]> InvitationSortColumns = SortSql.Map(
        (SortFields.OrganizationInvitations.Email, ["i.[Email]"]),
        (SortFields.OrganizationInvitations.RoleName, ["r.[Name]"]),
        (SortFields.OrganizationInvitations.RoleCode, ["r.[Code]"]),
        (SortFields.OrganizationInvitations.Status, [InvitationEffectiveStatusSql]),
        (SortFields.OrganizationInvitations.IsExpired,
            [$"CASE WHEN {InvitationEffectiveStatusSql} = 'Expired' THEN 1 ELSE 0 END"]),
        (SortFields.OrganizationInvitations.InvitedByName, ["inv.[FullName]"]),
        (SortFields.OrganizationInvitations.InvitedByEmail, ["inv.[Email]"]),
        (SortFields.OrganizationInvitations.AcceptedAt, ["i.[AcceptedAt]"]),
        (SortFields.OrganizationInvitations.CreatedAt, ["i.[CreatedAt]"]),
        (SortFields.OrganizationInvitations.ExpiresAt, ["i.[ExpiresAt]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<OrganizationInvitation> Invitations, int TotalCount)> GetInvitationsPagedAsync(
        Guid organizationId,
        Auth.Domain.ReadModels.Organizations.InvitationListFilter filter,
        DateTime now,
        int pageNumber,
        int pageSize,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One WHERE for the count and the page. No status keeps the list's historical content,
        // the stored-pending rows; statuses are bound as parameters, never written into the SQL.
        // The stored-status superset in front of the effective-status test is what an index on
        // [Status] can seek on: an effective status is only ever its own stored status, except
        // Expired, which is also stored Pending past its expiry.
        var statusClause = filter.Statuses.Count == 0
            ? "i.[Status] = 'Pending'"
            : $"i.[Status] IN @StoredStatuses AND {InvitationEffectiveStatusSql} IN @Statuses";
        // An application's own invitations only, when the filter names one (OI-194): a
        // membership-only invitation carries no application and never matches.
        var applicationClause = filter.ApplicationId is null ? string.Empty : " AND i.[ApplicationId] = @ApplicationId";
        var where = $"i.[OrganizationId] = @OrganizationId AND {statusClause}{applicationClause}";
        var statuses = filter.Statuses.Distinct().ToArray();
        var parameters = new DynamicParameters(new
        {
            OrganizationId = organizationId,
            filter.ApplicationId,
            Statuses = statuses.Select(status => status.ToString()).ToArray(),
            StoredStatuses = statuses
                .SelectMany(status => status == InvitationStatus.Expired
                    ? new[] { InvitationStatus.Pending, InvitationStatus.Expired }
                    : new[] { status })
                .Distinct()
                .Select(status => status.ToString())
                .ToArray(),
            Offset = (pageNumber - 1) * pageSize,
            PageSize = pageSize
        });
        // DATETIME2, as the column is: DATETIME would round @Now to 1/300 s, and a row expiring
        // inside that rounding could be filtered as one status and shown as the other.
        parameters.Add("Now", now, System.Data.DbType.DateTime2);

        var totalCount = await connection.ExecuteScalarAsync<int>($@"
            SELECT COUNT(1)
            FROM [dbo].[OrganizationInvitations] i
            WHERE {where}",
            parameters);

        var orderBy = SortSql.OrderBy(
            InvitationSortColumns, sortBy, sortDirection, "i.[CreatedAt] DESC", "i.[Id]");
        var dtos = await connection.QueryAsync<OrganizationInvitationDto>($@"
            SELECT
                i.[Id], i.[OrganizationId], i.[Email], i.[RoleId], i.[Token], i.[Status],
                i.[ExpiresAt], i.[InvitedBy], i.[AcceptedAt], i.[AcceptedByUserId], i.[CreatedAt],
                i.[ApplicationId], i.[ApplicationRoleId], i.[ApplicationRoleOutcome]
            FROM [dbo].[OrganizationInvitations] i
            LEFT JOIN [dbo].[Roles] r ON i.[RoleId] = r.[Id]
            LEFT JOIN [dbo].[Users] inv ON i.[InvitedBy] = inv.[Id]
            WHERE {where}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters);

        return (dtos.Select(dto => dto.ToEntity()).ToList().AsReadOnly(), totalCount);
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
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt],
                [ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]
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
                [ExpiresAt], [InvitedBy], [AcceptedAt], [AcceptedByUserId], [CreatedAt],
                [ApplicationId], [ApplicationRoleId]
            ) VALUES (
                @Id, @OrganizationId, @Email, @RoleId, @TokenHash, @Status,
                @ExpiresAt, @InvitedBy, @AcceptedAt, @AcceptedByUserId, @CreatedAt,
                @ApplicationId, @ApplicationRoleId
            )",
            new
            {
                invitation.Id,
                invitation.OrganizationId,
                Email = invitation.Email.Value,
                invitation.RoleId,
                invitation.TokenHash,
                Status = invitation.Status.ToString(),
                invitation.ExpiresAt,
                invitation.InvitedBy,
                invitation.AcceptedAt,
                invitation.AcceptedByUserId,
                invitation.CreatedAt,
                ApplicationId = invitation.ApplicationRole?.ApplicationId,
                ApplicationRoleId = invitation.ApplicationRole?.RoleId
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
                [Token] = @TokenHash,
                [Status] = @Status,
                [ExpiresAt] = @ExpiresAt,
                [AcceptedAt] = @AcceptedAt,
                [AcceptedByUserId] = @AcceptedByUserId
            WHERE [Id] = @Id",
            new
            {
                invitation.Id,
                invitation.TokenHash,
                Status = invitation.Status.ToString(),
                invitation.ExpiresAt,
                invitation.AcceptedAt,
                invitation.AcceptedByUserId
            });
    }

    /// <inheritdoc />
    public async Task<bool> AcceptInvitationAsync(
        OrganizationInvitation invitation,
        OrganizationUser? membership,
        OrganizationUserRole? applicationRole,
        CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection: BeginTransaction directly, never
        // Open() first, and every statement passes the transaction.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // The status flip goes first and only from Pending: of two concurrent
        // acceptances of one invitation, the second updates no row and stops here,
        // before it can insert a second membership or a second role. The application
        // role outcome rides in the same statement (OI-192): it is recorded exactly when
        // the acceptance is.
        var flipped = await connection.ExecuteAsync(@"
            UPDATE [dbo].[OrganizationInvitations] SET
                [Status] = @Status,
                [AcceptedAt] = @AcceptedAt,
                [AcceptedByUserId] = @AcceptedByUserId,
                [ApplicationRoleOutcome] = @ApplicationRoleOutcome
            WHERE [Id] = @Id AND [Status] = 'Pending'",
            new
            {
                invitation.Id,
                Status = invitation.Status.ToString(),
                invitation.AcceptedAt,
                invitation.AcceptedByUserId,
                ApplicationRoleOutcome = invitation.ApplicationRoleOutcome?.ToString()
            },
            transaction);

        if (flipped == 0)
        {
            transaction.Rollback();
            return false;
        }

        if (membership is not null)
        {
            await connection.ExecuteAsync(InsertMemberSql, MemberParameters(membership), transaction);
        }

        // The assignment's update-or-insert, inside this transaction (its locks last to the
        // commit): an earlier row of the same key, expired, deactivated or left by a former
        // membership, is restored with the invitation's terms instead of failing on the key.
        if (applicationRole is not null)
        {
            await connection.ExecuteAsync(
                "DECLARE @RowId UNIQUEIDENTIFIER;" + UpsertAppRoleSql,
                AppRoleParameters(applicationRole),
                transaction);
        }

        transaction.Commit();
        return true;
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

    #region Organization creation from an application

    /// <summary>
    /// The ONE definition of "this user's organization is set up for this
    /// application", over an <c>[dbo].[Organizations] o</c> row: owned by the user
    /// and active, the owner an active member, the application enabled and not
    /// expired, the creator role held there and not expired. The authorize
    /// endpoint asks it before issuing a code, and the provisioning transaction
    /// asserts it before committing; two definitions would let the step loop.
    /// Parameters: @UserId, @ApplicationId, @CreatorRoleId.
    /// </summary>
    private const string SetUpForApplicationPredicate = @"
                o.[OwnerId] = @UserId
            AND o.[IsActive] = 1
            AND EXISTS (
                SELECT 1 FROM [dbo].[OrganizationUsers] ou
                WHERE ou.[OrganizationId] = o.[Id] AND ou.[UserId] = @UserId AND ou.[IsActive] = 1
                  AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE()))
            AND EXISTS (
                SELECT 1 FROM [dbo].[OrganizationApplications] oa
                WHERE oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId AND oa.[IsActive] = 1
                  AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE()))
            AND EXISTS (
                SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                WHERE our.[OrganizationId] = o.[Id] AND our.[UserId] = @UserId
                  AND our.[ApplicationId] = @ApplicationId AND our.[RoleId] = @CreatorRoleId AND our.[IsActive] = 1
                  AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))";

    /// <summary>
    /// An organization the user may set up for an application instead of
    /// creating one: owned and active, not personal, the owner an active member,
    /// no expired subscription to the application that enabling it would leave
    /// expired, and no creator-role row that someone switched off or let expire.
    /// That last one is a revocation: the step never renews it, so an owner
    /// cannot take back a role an administrator withdrew by asking again.
    /// Parameters: @UserId, @ApplicationId, @CreatorRoleId.
    /// </summary>
    private const string EligibleOwnedOrganizationPredicate = @"
                o.[OwnerId] = @UserId
            AND o.[IsActive] = 1
            AND o.[IsAutoCreated] = 0
            AND EXISTS (
                SELECT 1 FROM [dbo].[OrganizationUsers] ou
                WHERE ou.[OrganizationId] = o.[Id] AND ou.[UserId] = @UserId AND ou.[IsActive] = 1
                  AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE()))
            AND NOT EXISTS (
                SELECT 1 FROM [dbo].[OrganizationApplications] oa
                WHERE oa.[OrganizationId] = o.[Id] AND oa.[ApplicationId] = @ApplicationId
                  AND oa.[ExpiresAt] IS NOT NULL AND oa.[ExpiresAt] <= GETUTCDATE())
            AND NOT EXISTS (
                SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                WHERE our.[OrganizationId] = o.[Id] AND our.[UserId] = @UserId
                  AND our.[ApplicationId] = @ApplicationId AND our.[RoleId] = @CreatorRoleId
                  AND (our.[IsActive] = 0 OR (our.[ExpiresAt] IS NOT NULL AND our.[ExpiresAt] <= GETUTCDATE())))";

    /// <inheritdoc />
    public async Task<int> CountSelfServiceOwnedAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Organizations]
            WHERE [OwnerId] = @UserId AND [IsAutoCreated] = 0",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task<Guid?> FindOrganizationSetUpForApplicationAsync(
        Guid userId,
        Guid applicationId,
        Guid creatorRoleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid?>(
            $"SELECT TOP 1 o.[Id] FROM [dbo].[Organizations] o WHERE {SetUpForApplicationPredicate} ORDER BY o.[CreatedAt]",
            new { UserId = userId, ApplicationId = applicationId, CreatorRoleId = creatorRoleId });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Auth.Domain.ReadModels.Organizations.OrganizationSetupCandidate>> GetOrganizationSetupCandidatesAsync(
        Guid userId,
        Guid applicationId,
        Guid creatorRoleId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<(Guid Id, string Name)>(
            $@"SELECT o.[Id], o.[Name] FROM [dbo].[Organizations] o
            WHERE {EligibleOwnedOrganizationPredicate}
              AND NOT ({SetUpForApplicationPredicate})
            ORDER BY o.[Name]",
            new { UserId = userId, ApplicationId = applicationId, CreatorRoleId = creatorRoleId });

        return rows
            .Select(row => new Auth.Domain.ReadModels.Organizations.OrganizationSetupCandidate(row.Id, row.Name))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<Auth.Domain.ReadModels.Organizations.OrganizationProvisioningOutcome> ProvisionForApplicationAsync(
        Auth.Domain.ReadModels.Organizations.OrganizationProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection: BeginTransaction directly,
        // never Open() first. Every statement below passes the transaction; a
        // write on another connection would commit on its own and break the
        // all-or-nothing promise this method exists for.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var keys = new { request.UserId, request.ApplicationId, request.CreatorRoleId };

        // The lock comes first and covers both paths: UPDLOCK makes a second
        // concurrent request for the same owner wait here, and HOLDLOCK keeps the
        // range locked until commit, so no organization can appear in it between
        // this count and the INSERT. The second request then finds the first one's
        // organization below instead of creating another.
        var ownedSelfService = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Organizations] WITH (UPDLOCK, HOLDLOCK)
            WHERE [OwnerId] = @UserId AND [IsAutoCreated] = 0",
            keys,
            transaction);

        var alreadySetUp = await connection.ExecuteScalarAsync<Guid?>(
            $"SELECT TOP 1 o.[Id] FROM [dbo].[Organizations] o WHERE {SetUpForApplicationPredicate} ORDER BY o.[CreatedAt]",
            keys,
            transaction);

        if (alreadySetUp is Guid existingSetUp)
        {
            transaction.Rollback();
            return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.AlreadySetUp, existingSetUp);
        }

        Guid organizationId;
        var created = request.NewOrganization is not null;

        if (request.NewOrganization is { } organization)
        {
            if (ownedSelfService >= request.MaxSelfServiceOrganizations)
            {
                transaction.Rollback();
                return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.LimitReached);
            }

            try
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO [dbo].[Organizations] (
                        [Id], [Code], [Name], [Description], [LogoUrl], [Website],
                        [ContactEmail], [OwnerId], [IsActive], [IsAutoCreated],
                        [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
                    ) VALUES (
                        @Id, @Code, @Name, @Description, @LogoUrl, @Website,
                        @ContactEmail, @OwnerId, @IsActive, 0,
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
                        organization.CreatedAt,
                        organization.CreatedBy,
                        organization.ModifiedAt,
                        organization.ModifiedBy
                    },
                    transaction);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Only the code can collide on a brand-new organization: its id is
                // fresh, and so is every row keyed by it.
                transaction.Rollback();
                return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.CodeTaken);
            }

            var membership = OrganizationUser.Create(
                organization.Id, request.UserId, request.OwnerRoleId, request.UserId);

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
                },
                transaction);

            organizationId = organization.Id;
        }
        else
        {
            // Someone else's organization, a personal one, or one the owner is no
            // longer an active member of all read the same: not found, nothing
            // written. UPDLOCK holds the row until commit.
            var eligible = await connection.ExecuteScalarAsync<Guid?>(
                $@"SELECT o.[Id] FROM [dbo].[Organizations] o WITH (UPDLOCK)
                WHERE o.[Id] = @OrganizationId AND {EligibleOwnedOrganizationPredicate}",
                new { request.UserId, request.ApplicationId, request.CreatorRoleId, OrganizationId = request.ExistingOrganizationId },
                transaction);

            if (eligible is not Guid eligibleId)
            {
                transaction.Rollback();
                return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.OrganizationNotEligible);
            }

            organizationId = eligibleId;

            // Enabled once and switched off since: switch it back on. A row that
            // is already active is left exactly as it is.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[OrganizationApplications] SET
                    [IsActive] = 1,
                    [ModifiedAt] = GETUTCDATE(),
                    [ModifiedBy] = @UserId
                WHERE [OrganizationId] = @OrganizationId
                  AND [ApplicationId] = @ApplicationId
                  AND [IsActive] = 0",
                new { request.UserId, request.ApplicationId, OrganizationId = organizationId },
                transaction);

            // A creator-role row that exists is never touched here: an active one
            // is already the grant, and an inactive or expired one made the
            // organization ineligible above.
        }

        // Inserted only where no row exists yet: a new organization has none, and
        // an existing one may already have either.
        var subscription = OrganizationApplication.Create(organizationId, request.ApplicationId, request.UserId);

        await connection.ExecuteAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[OrganizationApplications]
                WHERE [OrganizationId] = @OrganizationId AND [ApplicationId] = @ApplicationId)
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
            },
            transaction);

        // The creator-role grant. Deliberately NOT through OrganizationGrantGuard:
        // the user is not the authority here, and a new owner holds none of the
        // application's codes, so the guard would refuse every time. The authority
        // is the platform administrator who chose this role in the application's
        // settings, checked at that save to hold every permission it carries.
        var assignment = OrganizationUserRole.Create(
            organizationId, request.UserId, request.ApplicationId, request.CreatorRoleId, request.UserId);

        // The one insert of an organization application role outside UpsertAppRoleSql
        // (GrantWritersGuardTests): for a new organization, created in this same
        // transaction, no earlier row of the key can exist; for an existing one the
        // eligibility predicate refused any switched-off or expired creator-role row, and
        // a valid one is kept as it is.
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[OrganizationUserRoles]
                WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId
                  AND [ApplicationId] = @ApplicationId AND [RoleId] = @RoleId)
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
            },
            transaction);

        // The postcondition, asserted with the predicate authorize uses: if this
        // organization would not satisfy it, committing would send the user
        // straight back to this step.
        var nowSetUp = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM [dbo].[Organizations] o WHERE o.[Id] = @OrganizationId AND {SetUpForApplicationPredicate}",
            new { request.UserId, request.ApplicationId, request.CreatorRoleId, OrganizationId = organizationId },
            transaction);

        if (nowSetUp == 0)
        {
            transaction.Rollback();
            return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.OrganizationNotEligible);
        }

        transaction.Commit();
        return new(Auth.Domain.ReadModels.Organizations.OrganizationProvisioningStatus.Provisioned, organizationId, created);
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

        // Named for the COLUMN, which is still [Token]; the value in it is the
        // HMAC hash. This record mirrors the physical schema on purpose, and
        // ToEntity below hands it to the entity's tokenHash parameter, where the
        // name becomes honest again. Renaming the column would be a DACPAC
        // drop-and-add on a UNIQUE-constrained column for no gain.
        public string Token { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public Guid InvitedBy { get; init; }
        public DateTime? AcceptedAt { get; init; }
        public Guid? AcceptedByUserId { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid? ApplicationId { get; init; }
        public Guid? ApplicationRoleId { get; init; }
        public string? ApplicationRoleOutcome { get; init; }

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
                CreatedAt,
                InvitationApplicationRole.FromNullable(ApplicationId, ApplicationRoleId),
                ApplicationRoleOutcome is null
                    ? null
                    : Enum.Parse<InvitationApplicationRoleOutcome>(ApplicationRoleOutcome));
        }
    }

    #endregion
}
