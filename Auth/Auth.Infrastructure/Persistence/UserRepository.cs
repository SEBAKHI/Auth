using System.Data;
using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Dapper;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the user repository.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly PasswordSettings _passwordSettings;

    public UserRepository(
        IDbConnectionFactory connectionFactory,
        IOptions<PasswordSettings> passwordSettings)
    {
        _connectionFactory = connectionFactory;
        _passwordSettings = passwordSettings.Value;
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserById] @UserId",
            new { UserId = id });

        return result?.ToUser();
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdIncludeDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(@"
            SELECT
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash],
                [FirstName], [LastName], [FullName] AS [DisplayName], [PhoneNumber],
                [PreferredLanguage], [TimeZone], [Theme],
                [IsEmailConfirmed] AS [EmailConfirmed],
                [IsPhoneConfirmed] AS [PhoneConfirmed],
                [IsTwoFactorEnabled] AS [TwoFactorEnabled],
                [Status], [FailedLoginAttempts],
                [LockoutEndUtc] AS [LockoutEnd],
                [LastLoginUtc] AS [LastLoginAt],
                [LastPasswordChangeUtc] AS [PasswordChangedAt],
                [MustChangePassword],
                [ProfileImageUrl], [LastLoginIp], [PasswordExpiresUtc],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy],
                [IsDeleted], [DeletedAt]
            FROM [dbo].[Users]
            WHERE [Id] = @Id",
            new { Id = id });

        return result?.ToUser();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var results = await connection.QueryAsync<UserDto>(@"
            SELECT
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash],
                [FirstName], [LastName], [FullName] AS [DisplayName], [PhoneNumber],
                [PreferredLanguage], [TimeZone], [Theme],
                [IsEmailConfirmed] AS [EmailConfirmed],
                [IsPhoneConfirmed] AS [PhoneConfirmed],
                [IsTwoFactorEnabled] AS [TwoFactorEnabled],
                [Status], [FailedLoginAttempts],
                [LockoutEndUtc] AS [LockoutEnd],
                [LastLoginUtc] AS [LastLoginAt],
                [LastPasswordChangeUtc] AS [PasswordChangedAt],
                [MustChangePassword],
                [ProfileImageUrl], [LastLoginIp], [PasswordExpiresUtc],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Users]
            WHERE [Id] IN @Ids",
            new { Ids = ids });

        return results.Select(dto => dto.ToUser()).ToList();
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserByEmail] @Email",
            new { Email = email.ToUpperInvariant() });

        return result?.ToUser();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM [dbo].[Users] WHERE [NormalizedEmail] = @NormalizedEmail",
            new { NormalizedEmail = email.ToUpperInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Users] (
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName],
                [PhoneNumber], [PreferredLanguage], [TimeZone], [Theme],
                [IsEmailConfirmed], [IsPhoneConfirmed], [IsTwoFactorEnabled],
                [Status], [FailedLoginAttempts], [LockoutEndUtc], [LastLoginUtc],
                [LastPasswordChangeUtc], [MustChangePassword], [ProfileImageUrl],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Username, @Email, @NormalizedEmail, @PasswordHash, @FirstName, @LastName,
                @PhoneNumber, @PreferredLanguage, @TimeZone, @Theme,
                @IsEmailConfirmed, @IsPhoneConfirmed, @IsTwoFactorEnabled,
                @Status, @FailedLoginAttempts, @LockoutEndUtc, @LastLoginUtc,
                @LastPasswordChangeUtc, @MustChangePassword, @ProfileImageUrl,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                user.Id,
                Username = user.Email.Value.Split('@')[0],
                Email = user.Email.Value,
                user.NormalizedEmail,
                user.PasswordHash,
                user.FirstName,
                user.LastName,
                PhoneNumber = user.PhoneNumber?.Value,
                user.PreferredLanguage,
                user.TimeZone,
                user.Theme,
                IsEmailConfirmed = user.EmailConfirmed,
                IsPhoneConfirmed = user.PhoneConfirmed,
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                Status = (int)user.Status,
                user.FailedLoginAttempts,
                LockoutEndUtc = user.LockoutEnd,
                LastLoginUtc = user.LastLoginAt,
                LastPasswordChangeUtc = user.PasswordChangedAt,
                user.MustChangePassword,
                user.ProfileImageUrl,
                user.CreatedAt,
                user.CreatedBy,
                user.ModifiedAt,
                user.ModifiedBy
            });

        return user;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [Email] = @Email,
                [NormalizedEmail] = @NormalizedEmail,
                [PasswordHash] = @PasswordHash,
                [FirstName] = @FirstName,
                [LastName] = @LastName,
                [PhoneNumber] = @PhoneNumber,
                [Status] = @Status,
                [IsEmailConfirmed] = @IsEmailConfirmed,
                [IsPhoneConfirmed] = @IsPhoneConfirmed,
                [IsTwoFactorEnabled] = @IsTwoFactorEnabled,
                [FailedLoginAttempts] = @FailedLoginAttempts,
                [LockoutEndUtc] = @LockoutEndUtc,
                [LastLoginUtc] = @LastLoginUtc,
                [LastPasswordChangeUtc] = @LastPasswordChangeUtc,
                [MustChangePassword] = @MustChangePassword,
                [ProfileImageUrl] = @ProfileImageUrl,
                [LastLoginIp] = @LastLoginIp,
                [PasswordExpiresUtc] = @PasswordExpiresUtc,
                [PreferredLanguage] = @PreferredLanguage,
                [TimeZone] = @TimeZone,
                [Theme] = @Theme,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                user.Id,
                Email = user.Email.Value,
                user.NormalizedEmail,
                user.PasswordHash,
                user.FirstName,
                user.LastName,
                PhoneNumber = user.PhoneNumber?.Value,
                Status = (int)user.Status,
                IsEmailConfirmed = user.EmailConfirmed,
                IsPhoneConfirmed = user.PhoneConfirmed,
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                user.FailedLoginAttempts,
                LockoutEndUtc = user.LockoutEnd,
                LastLoginUtc = user.LastLoginAt,
                LastPasswordChangeUtc = user.PasswordChangedAt,
                user.MustChangePassword,
                user.ProfileImageUrl,
                user.LastLoginIp,
                PasswordExpiresUtc = user.PasswordExpiresUtc,
                user.PreferredLanguage,
                user.TimeZone,
                user.Theme,
                user.ModifiedAt,
                user.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Soft delete by setting IsDeleted flag
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [IsDeleted] = 1,
                [DeletedAt] = GETUTCDATE(),
                [DeletedBy] = @Id
            WHERE [Id] = @Id",
            new { Id = id });
    }

    /// <inheritdoc />
    public async Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Re-verify eligibility inside the transaction: UPDLOCK/HOLDLOCK
        // serializes with any concurrent write on the row, so a live account
        // can never race past the soft-deleted check and get purged.
        var eligible = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @Id AND [IsDeleted] = 1",
            new { Id = id },
            transaction);

        if (eligible == 0)
        {
            transaction.Rollback();
            return false;
        }

        // Every table below either references Users through a non-cascading
        // foreign key or carries a loose user reference (AuditLogs,
        // NotificationOutbox, RevokedTokens). Rows the user owns are deleted;
        // actor references on records that belong to other entities are
        // reattributed to the system account so those rows keep resolving.
        // UserHardDeleteSqlTests guards this list against schema drift.
        await connection.ExecuteAsync(@"
            -- Crypto-shred first: destroying the per-user DEK renders every
            -- ciphertext under it (phone number, TOTP secret, provider refresh
            -- token) unrecoverable, in this database and in every backup.
            DELETE FROM [dbo].[UserEncryptionKeys] WHERE [UserId] = @Id;

            -- Credentials, sessions and security artifacts owned by the user
            DELETE FROM [dbo].[RefreshTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserSessions] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[IdpSessions] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[AuthorizationCodes] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[LoginAttempts] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserExternalLogins] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[EmailVerificationTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[PasswordResetTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[PasswordHistory] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[TwoFactorChallenges] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[RevokedTokens] WHERE [RevocationKey] = CONVERT(NVARCHAR(200), @Id);

            -- Platform-level assignments
            DELETE FROM [dbo].[UserRoles] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserPermissions] WHERE [UserId] = @Id;

            -- Organization memberships and artifacts the user authored
            DELETE FROM [dbo].[OrganizationUserRoles] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[OrganizationUserPermissions] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[OrganizationUsers] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[OrganizationInvitations] WHERE [InvitedBy] = @Id;
            DELETE FROM [dbo].[OwnershipTransferCodes] WHERE [TargetUserId] = @Id OR [InitiatedBy] = @Id;

            -- Notifications addressed to the user
            DELETE FROM [dbo].[NotificationOutbox] WHERE [RecipientUserId] = @Id;

            -- The user's audit trail: rows about them and rows they performed
            DELETE FROM [dbo].[AuditLogs] WHERE [UserId] = @Id OR [PerformedBy] = @Id;

            -- Actor references on surviving records of other entities
            UPDATE [dbo].[OrganizationApplications] SET [EnabledBy] = @SystemUserId WHERE [EnabledBy] = @Id;
            UPDATE [dbo].[OrganizationUsers] SET [InvitedBy] = @SystemUserId WHERE [InvitedBy] = @Id;
            UPDATE [dbo].[OrganizationUserRoles] SET [AssignedBy] = @SystemUserId WHERE [AssignedBy] = @Id;
            UPDATE [dbo].[OrganizationUserPermissions] SET [GrantedBy] = @SystemUserId WHERE [GrantedBy] = @Id;
            UPDATE [dbo].[OrganizationInvitations] SET [AcceptedByUserId] = NULL WHERE [AcceptedByUserId] = @Id;

            -- The account row last; IsDeleted = 1 is the final in-database guard
            DELETE FROM [dbo].[Users] WHERE [Id] = @Id AND [IsDeleted] = 1;",
            new { Id = id, SystemUserId = WellKnownUserIds.System },
            transaction);

        transaction.Commit();
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string[]> PagedSortColumns = SortSql.Map(
        (SortFields.Users.Name, ["[FullName]"]),
        (SortFields.Users.DisplayName, ["[FullName]"]),
        (SortFields.Users.FirstName, ["[FirstName]"]),
        (SortFields.Users.LastName, ["[LastName]"]),
        (SortFields.Users.Email, ["[Email]"]),
        (SortFields.Users.PhoneNumber, ["[PhoneNumber]"]),
        (SortFields.Users.Status, ["[Status]"]),
        (SortFields.Users.EmailConfirmed, ["[IsEmailConfirmed]"]),
        (SortFields.Users.PhoneConfirmed, ["[IsPhoneConfirmed]"]),
        (SortFields.Users.TwoFactorEnabled, ["[IsTwoFactorEnabled]"]),
        (SortFields.Users.PreferredLanguage, ["[PreferredLanguage]"]),
        (SortFields.Users.TimeZone, ["[TimeZone]"]),
        (SortFields.Users.CreatedAt, ["[CreatedAt]"]),
        (SortFields.Users.ModifiedAt, ["[ModifiedAt]"]),
        (SortFields.Users.LastLoginAt, ["[LastLoginUtc]"]));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var offset = (pageNumber - 1) * pageSize;
        var searchPattern = string.IsNullOrEmpty(searchTerm) ? null : $"%{searchTerm}%";
        var orderBy = SortSql.OrderBy(
            PagedSortColumns, sortBy, sortDirection, "[CreatedAt] DESC", "[Id]");

        var sql = $@"
            SELECT COUNT(1) FROM [dbo].[Users]
            WHERE (@IncludeDeleted = 1 OR [IsDeleted] = 0)
              AND (@SearchPattern IS NULL OR
                   [Email] LIKE @SearchPattern OR
                   [FirstName] LIKE @SearchPattern OR
                   [LastName] LIKE @SearchPattern);

            SELECT
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash],
                [FirstName], [LastName], [FullName] AS [DisplayName], [PhoneNumber],
                [PreferredLanguage], [TimeZone], [Theme],
                [IsEmailConfirmed] AS [EmailConfirmed],
                [IsPhoneConfirmed] AS [PhoneConfirmed],
                [IsTwoFactorEnabled] AS [TwoFactorEnabled],
                [Status], [FailedLoginAttempts],
                [LockoutEndUtc] AS [LockoutEnd],
                [LastLoginUtc] AS [LastLoginAt],
                [LastPasswordChangeUtc] AS [PasswordChangedAt],
                [MustChangePassword],
                [ProfileImageUrl], [LastLoginIp], [PasswordExpiresUtc],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy],
                [IsDeleted], [DeletedAt]
            FROM [dbo].[Users]
            WHERE (@IncludeDeleted = 1 OR [IsDeleted] = 0)
              AND (@SearchPattern IS NULL OR
                   [Email] LIKE @SearchPattern OR
                   [FirstName] LIKE @SearchPattern OR
                   [LastName] LIKE @SearchPattern)
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            IncludeDeleted = includeDeleted,
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var users = (await multi.ReadAsync<UserDto>()).Select(dto => dto.ToUser()).ToList();

        return (users, totalCount);
    }

    /// <inheritdoc />
    public async Task RecordSuccessfulLoginAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [LastLoginUtc] = GETUTCDATE(),
                [LastLoginIp] = @IpAddress,
                [FailedLoginAttempts] = 0,
                [LockoutEndUtc] = NULL,
                [ModifiedAt] = GETUTCDATE()
            WHERE [Id] = @UserId",
            new { UserId = userId, IpAddress = ipAddress });
    }

    /// <inheritdoc />
    public async Task RecordFailedLoginAsync(
        Guid userId,
        int maxAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Increment failed login attempts and apply lockout if max attempts reached
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users]
            SET [FailedLoginAttempts] = [FailedLoginAttempts] + 1,
                [LockoutEndUtc] = CASE
                    WHEN [FailedLoginAttempts] + 1 >= @MaxAttempts
                    THEN DATEADD(MINUTE, @LockoutMinutes, GETUTCDATE())
                    ELSE [LockoutEndUtc]
                END,
                [Status] = CASE
                    WHEN [FailedLoginAttempts] + 1 >= @MaxAttempts
                    THEN 3  -- Locked
                    ELSE [Status]
                END,
                [ModifiedAt] = GETUTCDATE()
            WHERE [Id] = @UserId",
            new
            {
                UserId = userId,
                MaxAttempts = maxAttempts,
                LockoutMinutes = (int)lockoutDuration.TotalMinutes
            });
    }

    /// <inheritdoc />
    public async Task UnlockAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [Status] = @Status,
                [FailedLoginAttempts] = 0,
                [LockoutEndUtc] = NULL,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @UserId",
            new
            {
                UserId = userId,
                Status = (int)UserStatus.Active,
                ModifiedBy = modifiedBy
            });
    }

    /// <inheritdoc />
    public async Task UpdatePasswordAsync(
        Guid userId,
        string passwordHash,
        Guid modifiedBy,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [PasswordHash] = @PasswordHash,
                [LastPasswordChangeUtc] = GETUTCDATE(),
                [MustChangePassword] = 0,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @UserId",
            new
            {
                UserId = userId,
                PasswordHash = passwordHash,
                ModifiedBy = modifiedBy
            });
    }

    /// <inheritdoc />
    public async Task ConfirmEmailAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [IsEmailConfirmed] = 1,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @UserId",
            new
            {
                UserId = userId,
                ModifiedBy = modifiedBy
            });
    }

    #region User Roles

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<UserRoleInternalDto>(@"
            SELECT * FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId AND [IsActive] = 1",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<UserRole?> GetUserRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var sql = @"
            SELECT * FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId AND [RoleId] = @RoleId AND [IsActive] = 1";

        if (applicationId.HasValue)
            sql += " AND [ApplicationId] = @ApplicationId";
        else
            sql += " AND [ApplicationId] IS NULL";

        var dto = await connection.QueryFirstOrDefaultAsync<UserRoleInternalDto>(sql, new
        {
            UserId = userId,
            RoleId = roleId,
            ApplicationId = applicationId
        });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<UserRole> AssignRoleAsync(UserRole userRole, CancellationToken cancellationToken)
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

        return userRole;
    }

    /// <inheritdoc />
    public async Task RemoveRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Hard delete: UQ_UserRoles spans (UserId, RoleId, ApplicationId) without an
        // [IsActive] filter, so a deactivated row would block re-assigning the same
        // role later. Removals are recorded in the audit log, not in this table.
        var sql = @"
            DELETE FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId AND [RoleId] = @RoleId";

        if (applicationId.HasValue)
            sql += " AND [ApplicationId] = @ApplicationId";
        else
            sql += " AND [ApplicationId] IS NULL";

        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            RoleId = roleId,
            ApplicationId = applicationId
        });
    }

    /// <inheritdoc />
    public async Task<bool> HasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[UserRoles]
            WHERE [UserId] = @UserId AND [RoleId] = @RoleId AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, RoleId = roleId });

        return count > 0;
    }

    #endregion

    #region User Permissions (Direct Grants)

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserPermission>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dtos = await connection.QueryAsync<UserPermissionInternalDto>(@"
            SELECT * FROM [dbo].[UserPermissions]
            WHERE [UserId] = @UserId AND [IsActive] = 1",
            new { UserId = userId });

        return dtos.Select(dto => dto.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<UserPermission?> GetUserPermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var sql = @"
            SELECT * FROM [dbo].[UserPermissions]
            WHERE [UserId] = @UserId AND [PermissionId] = @PermissionId AND [IsActive] = 1";

        if (applicationId.HasValue)
            sql += " AND [ApplicationId] = @ApplicationId";
        else
            sql += " AND [ApplicationId] IS NULL";

        var dto = await connection.QueryFirstOrDefaultAsync<UserPermissionInternalDto>(sql, new
        {
            UserId = userId,
            PermissionId = permissionId,
            ApplicationId = applicationId
        });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<UserPermission> GrantPermissionAsync(UserPermission userPermission, CancellationToken cancellationToken)
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

        return userPermission;
    }

    /// <inheritdoc />
    public async Task RevokePermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Hard delete: UQ_UserPermissions spans (UserId, PermissionId, ApplicationId)
        // without an [IsActive] filter, so a deactivated row would block re-granting
        // the same permission later. Revocations are recorded in the audit log.
        var sql = @"
            DELETE FROM [dbo].[UserPermissions]
            WHERE [UserId] = @UserId AND [PermissionId] = @PermissionId";

        if (applicationId.HasValue)
            sql += " AND [ApplicationId] = @ApplicationId";
        else
            sql += " AND [ApplicationId] IS NULL";

        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            PermissionId = permissionId,
            ApplicationId = applicationId
        });
    }

    /// <inheritdoc />
    public async Task<bool> HasDirectPermissionAsync(Guid userId, Guid permissionId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM [dbo].[UserPermissions]
            WHERE [UserId] = @UserId AND [PermissionId] = @PermissionId AND [IsActive] = 1
              AND ([ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE())",
            new { UserId = userId, PermissionId = permissionId });

        return count > 0;
    }

    #endregion

    // Internal DTO for mapping from database
    private record UserDto
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string NormalizedEmail { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? PhoneNumber { get; init; }
        public int Status { get; init; }
        public bool EmailConfirmed { get; init; }
        public bool PhoneConfirmed { get; init; }
        public bool TwoFactorEnabled { get; init; }
        public string? TwoFactorSecret { get; init; }
        public int FailedLoginAttempts { get; init; }
        public DateTime? LockoutEnd { get; init; }
        public DateTime? LastLoginAt { get; init; }
        public DateTime? PasswordChangedAt { get; init; }
        public bool MustChangePassword { get; init; }
        public string? PreferredLanguage { get; init; }
        public string? TimeZone { get; init; }
        public string? Theme { get; init; }
        public string? Metadata { get; init; }
        public bool IsSystemUser { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }
        public string? ProfileImageUrl { get; init; }
        public string? LastLoginIp { get; init; }
        public DateTime? PasswordExpiresUtc { get; init; }
        public bool IsDeleted { get; init; }
        public DateTime? DeletedAt { get; init; }

        public User ToUser() => new(
            Id,
            Email,
            NormalizedEmail,
            PasswordHash,
            FirstName,
            LastName,
            DisplayName,
            PhoneNumber,
            (UserStatus)Status,
            EmailConfirmed,
            PhoneConfirmed,
            TwoFactorEnabled,
            TwoFactorSecret,
            FailedLoginAttempts,
            LockoutEnd,
            LastLoginAt,
            PasswordChangedAt,
            MustChangePassword,
            PreferredLanguage,
            TimeZone,
            Metadata,
            IsSystemUser,
            CreatedAt,
            CreatedBy,
            ModifiedAt,
            ModifiedBy,
            ProfileImageUrl,
            LastLoginIp,
            PasswordExpiresUtc,
            Theme ?? "system",
            IsDeleted,
            DeletedAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserApplicationAccess>> GetUserApplicationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Same access semantics as OrganizationRepository.HasAppAccessAsync,
        // generalized to a list and unioned with direct app-scoped role assignments.
        var rows = await connection.QueryAsync<UserApplicationAccess>(@"
            SELECT
                a.[Id] AS [ApplicationId],
                a.[Code],
                a.[Name],
                a.[LogoUrl],
                a.[IsActive],
                CAST(MAX(src.[ViaOrganization]) AS BIT) AS [ViaOrganization],
                CAST(MAX(src.[ViaDirect]) AS BIT) AS [ViaDirect]
            FROM (
                SELECT oa.[ApplicationId], 1 AS [ViaOrganization], 0 AS [ViaDirect]
                FROM [dbo].[OrganizationUsers] ou
                INNER JOIN [dbo].[Organizations] o ON ou.[OrganizationId] = o.[Id]
                INNER JOIN [dbo].[OrganizationApplications] oa ON o.[Id] = oa.[OrganizationId]
                WHERE ou.[UserId] = @UserId
                  AND ou.[IsActive] = 1 AND o.[IsActive] = 1 AND oa.[IsActive] = 1
                  AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())
                  AND (oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())
                  AND (
                      EXISTS (
                          SELECT 1 FROM [dbo].[OrganizationUserRoles] our
                          WHERE our.[OrganizationId] = o.[Id]
                            AND our.[UserId] = @UserId
                            AND our.[ApplicationId] = oa.[ApplicationId]
                            AND our.[IsActive] = 1
                            AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > GETUTCDATE()))
                      OR EXISTS (
                          SELECT 1 FROM [dbo].[OrganizationUserPermissions] oup
                          WHERE oup.[OrganizationId] = o.[Id]
                            AND oup.[UserId] = @UserId
                            AND oup.[ApplicationId] = oa.[ApplicationId]
                            AND oup.[IsActive] = 1
                            AND (oup.[ExpiresAt] IS NULL OR oup.[ExpiresAt] > GETUTCDATE()))
                  )
                UNION ALL
                SELECT ur.[ApplicationId], 0, 1
                FROM [dbo].[UserRoles] ur
                WHERE ur.[UserId] = @UserId
                  AND ur.[ApplicationId] IS NOT NULL
                  AND ur.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
            ) src
            INNER JOIN [dbo].[Applications] a ON src.[ApplicationId] = a.[Id]
            WHERE a.[IsDeleted] = 0
            GROUP BY a.[Id], a.[Code], a.[Name], a.[LogoUrl], a.[IsActive]",
            new { UserId = userId });

        return rows.ToList();
    }

    private record UserRoleInternalDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid RoleId { get; init; }
        public Guid? ApplicationId { get; init; }
        public DateTime AssignedAt { get; init; }
        public Guid AssignedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public bool IsActive { get; init; }

        public UserRole ToEntity() => new(
            Id,
            UserId,
            RoleId,
            ApplicationId,
            AssignedAt,
            AssignedBy,
            ExpiresAt,
            IsActive);
    }

    private record UserPermissionInternalDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid PermissionId { get; init; }
        public Guid? ApplicationId { get; init; }
        public DateTime GrantedAt { get; init; }
        public Guid GrantedBy { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public bool IsActive { get; init; }

        public UserPermission ToEntity() => new(
            Id,
            UserId,
            PermissionId,
            ApplicationId,
            GrantedAt,
            GrantedBy,
            ExpiresAt,
            IsActive);
    }
}
