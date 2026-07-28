using System.Data;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
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
    private readonly IIdentifierHasher _identifierHasher;
    private readonly AccountDeletionSettings _accountDeletionSettings;
    private readonly IPerUserCryptoService _perUserCrypto;

    public UserRepository(
        IDbConnectionFactory connectionFactory,
        IOptions<PasswordSettings> passwordSettings,
        IIdentifierHasher identifierHasher,
        IOptions<AccountDeletionSettings> accountDeletionSettings,
        IPerUserCryptoService perUserCrypto)
    {
        _connectionFactory = connectionFactory;
        _passwordSettings = passwordSettings.Value;
        _identifierHasher = identifierHasher;
        _accountDeletionSettings = accountDeletionSettings.Value;
        _perUserCrypto = perUserCrypto;
    }

    /// <summary>
    /// Dual-read at the repository boundary: PhoneNumber is stored as v2
    /// per-user ciphertext (crypto-shredded with the account); rows not yet
    /// touched by the one-time migration still hold plaintext and pass
    /// through unchanged. Callers always see the plaintext value.
    /// </summary>
    private async Task<UserDto?> WithDecryptedPhoneAsync(UserDto? dto, CancellationToken cancellationToken)
    {
        if (dto?.PhoneNumber is not null && _perUserCrypto.IsEncrypted(dto.PhoneNumber))
        {
            dto.PhoneNumber = await _perUserCrypto.DecryptAsync(
                dto.Id, dto.PhoneNumber, EncryptedFieldPurpose.UserPhoneNumber, cancellationToken);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserById] @UserId",
            new { UserId = id });

        return (await WithDecryptedPhoneAsync(result, cancellationToken))?.ToUser();
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

        return (await WithDecryptedPhoneAsync(result, cancellationToken))?.ToUser();
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

        var dtos = results.ToList();
        foreach (var dto in dtos)
        {
            await WithDecryptedPhoneAsync(dto, cancellationToken);
        }

        return dtos.Select(dto => dto.ToUser()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid Id, string Email, string? DisplayName, string? FirstName, string? PreferredLanguage)>>
        GetActiveNotificationRecipientsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Deliberately a minimal projection: a platform-wide send must not
        // hydrate full entities or decrypt phone numbers for every user.
        var rows = await connection.QueryAsync<(Guid, string, string?, string?, string?)>(@"
            SELECT [Id], [Email], [FullName], [FirstName], [PreferredLanguage]
            FROM [dbo].[Users]
            WHERE [Status] = @Active AND [IsDeleted] = 0 AND [IsEmailConfirmed] = 1
            ORDER BY [CreatedAt]",
            new { Active = (int)UserStatus.Active });

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserByEmail] @Email",
            new { Email = email.ToUpperInvariant() });

        return (await WithDecryptedPhoneAsync(result, cancellationToken))?.ToUser();
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailIncludeDeletedAsync(string email, CancellationToken cancellationToken)
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
            WHERE [NormalizedEmail] = @NormalizedEmail",
            new { NormalizedEmail = email.ToUpperInvariant() });

        return (await WithDecryptedPhoneAsync(result, cancellationToken))?.ToUser();
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users]
            SET [IsDeleted] = 0, [DeletedAt] = NULL, [DeletedBy] = NULL,
                [ModifiedAt] = GETUTCDATE(), [ModifiedBy] = @Id
            WHERE [Id] = @Id AND [IsDeleted] = 1",
            new { Id = id });
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
                PhoneNumber = (string?)null,
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

        // The per-user DEK row has an FK to Users, so the phone can only be
        // encrypted after the account row exists: insert without it, then
        // write the ciphertext.
        if (!string.IsNullOrEmpty(user.PhoneNumber?.Value))
        {
            var encryptedPhone = await _perUserCrypto.EncryptAsync(
                user.Id, user.PhoneNumber.Value, EncryptedFieldPurpose.UserPhoneNumber, cancellationToken);
            await connection.ExecuteAsync(
                "UPDATE [dbo].[Users] SET [PhoneNumber] = @PhoneNumber WHERE [Id] = @Id",
                new { user.Id, PhoneNumber = encryptedPhone });
        }

        return user;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var encryptedPhone = string.IsNullOrEmpty(user.PhoneNumber?.Value)
            ? null
            : await _perUserCrypto.EncryptAsync(
                user.Id, user.PhoneNumber.Value, EncryptedFieldPurpose.UserPhoneNumber, cancellationToken);

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
                PhoneNumber = encryptedPhone,
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

        // Re-verify eligibility inside the transaction and snapshot the
        // identifiers for the tombstone in the same statement: UPDLOCK/HOLDLOCK
        // serializes with any concurrent write on the row, so a live account
        // can never race past the soft-deleted check and get purged, and the
        // hashes are computed from the row being destroyed, never a stale
        // caller-side snapshot.
        var identifiers = await connection.QuerySingleOrDefaultAsync<(string NormalizedEmail, string Username)>(@"
            SELECT [NormalizedEmail], [Username] FROM [dbo].[Users] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @Id AND [IsDeleted] = 1",
            new { Id = id },
            transaction);

        // Both columns are NOT NULL, so a null field means "no eligible row".
        if (identifiers.NormalizedEmail is null)
        {
            transaction.Rollback();
            return false;
        }

        var emailHash = _identifierHasher.HashEmail(identifiers.NormalizedEmail);
        var usernameHash = _identifierHasher.HashUsername(identifiers.Username);

        // Staged destruction. Every table below either references Users
        // through a non-cascading foreign key or carries a loose user
        // reference (AuditLogs, NotificationOutbox, RevokedTokens). Rows the
        // user owns are deleted; the audit/login history is anonymized in
        // place; actor references on records that belong to other entities are
        // reattributed to the system account so those rows keep resolving.
        // AccountDeletionRequests rows are retained untouched as destruction
        // evidence. UserHardDeleteSqlTests guards this list against schema drift.
        await connection.ExecuteAsync(@"
            -- Permanent zero-PII tombstone (idempotent MERGE): the identifier
            -- reservation and the restore re-apply anchor. Written before
            -- anything is destroyed so a mid-purge failure never loses it.
            MERGE [dbo].[AccountDeletionTombstones] WITH (HOLDLOCK) AS [target]
            USING (SELECT @EmailHash AS [EmailHash]) AS [source]
            ON [target].[EmailHash] = [source].[EmailHash]
            WHEN NOT MATCHED THEN
                INSERT ([EmailHash], [UsernameHash], [DeletedAtUtc], [PolicyVersion])
                VALUES (@EmailHash, @UsernameHash, GETUTCDATE(), @PolicyVersion);

            -- Crypto-shred: destroying the per-user DEK renders every
            -- ciphertext under it (phone number, TOTP secret, provider refresh
            -- token) unrecoverable, in this database and in every backup.
            DELETE FROM [dbo].[UserEncryptionKeys] WHERE [UserId] = @Id;

            -- Credentials, sessions and security artifacts owned by the user
            DELETE FROM [dbo].[RefreshTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserSessions] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[IdpSessions] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[AuthorizationCodes] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserExternalLogins] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[EmailVerificationTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[PasswordResetTokens] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[AccountDeletionVerifications] WHERE [UserId] = @Id;
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

            -- Class B/C: the audit and login-attempt history is anonymized,
            -- never deleted — the security record survives with identity and
            -- PII payloads stripped.
            UPDATE [dbo].[AuditLogs]
            SET [UserId] = NULL, [OldValues] = NULL, [NewValues] = NULL,
                [Details] = NULL, [IpAddress] = NULL, [UserAgent] = NULL
            WHERE [UserId] = @Id;
            UPDATE [dbo].[AuditLogs]
            SET [PerformedBy] = @SystemUserId, [IpAddress] = NULL, [UserAgent] = NULL
            WHERE [PerformedBy] = @Id;
            UPDATE [dbo].[LoginAttempts]
            SET [UserId] = NULL, [Username] = N'[deleted]'
            WHERE [UserId] = @Id;

            -- Actor references on surviving records of other entities
            UPDATE [dbo].[OrganizationApplications] SET [EnabledBy] = @SystemUserId WHERE [EnabledBy] = @Id;
            UPDATE [dbo].[OrganizationUsers] SET [InvitedBy] = @SystemUserId WHERE [InvitedBy] = @Id;
            UPDATE [dbo].[OrganizationUserRoles] SET [AssignedBy] = @SystemUserId WHERE [AssignedBy] = @Id;
            UPDATE [dbo].[OrganizationUserPermissions] SET [GrantedBy] = @SystemUserId WHERE [GrantedBy] = @Id;
            UPDATE [dbo].[OrganizationInvitations] SET [AcceptedByUserId] = NULL WHERE [AcceptedByUserId] = @Id;

            -- The account row last; IsDeleted = 1 is the final in-database guard
            DELETE FROM [dbo].[Users] WHERE [Id] = @Id AND [IsDeleted] = 1;",
            new
            {
                Id = id,
                SystemUserId = WellKnownUserIds.System,
                EmailHash = emailHash,
                UsernameHash = usernameHash,
                PolicyVersion = _accountDeletionSettings.PolicyVersion
            },
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
        var dtos = (await multi.ReadAsync<UserDto>()).ToList();
        foreach (var dto in dtos)
        {
            await WithDecryptedPhoneAsync(dto, cancellationToken);
        }

        var users = dtos.Select(dto => dto.ToUser()).ToList();

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
        // Nullable with NO default: DB NULL means an external-only account,
        // and Dapper leaves a property default in place on NULL — an empty
        // string here would make every "has no password" guard dead code.
        public string? PasswordHash { get; init; }
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        // Settable: WithDecryptedPhoneAsync replaces the stored ciphertext
        // with plaintext before the DTO is mapped to the entity.
        public string? PhoneNumber { get; set; }
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
