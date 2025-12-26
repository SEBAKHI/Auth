using System.Data;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Enums;
using Auth_Lib.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Extensions.Options;

namespace Auth_Lib.Infrastructure.Data;

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
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserById] @UserId",
            new { UserId = id });

        return result?.ToUser();
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<UserDto>(
            "EXEC [dbo].[sp_GetUserByEmail] @Email",
            new { Email = email.ToUpperInvariant() });

        return result?.ToUser();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM [dbo].[Users] WHERE [NormalizedEmail] = @NormalizedEmail",
            new { NormalizedEmail = email.ToUpperInvariant() });

        return count > 0;
    }

    /// <inheritdoc />
    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[Users] (
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName],
                [PhoneNumber], [PreferredLanguage], [TimeZone],
                [IsEmailConfirmed], [IsPhoneConfirmed], [IsTwoFactorEnabled],
                [Status], [FailedLoginAttempts], [LockoutEndUtc], [LastLoginUtc],
                [LastPasswordChangeUtc], [MustChangePassword],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            ) VALUES (
                @Id, @Username, @Email, @NormalizedEmail, @PasswordHash, @FirstName, @LastName,
                @PhoneNumber, @PreferredLanguage, @TimeZone,
                @IsEmailConfirmed, @IsPhoneConfirmed, @IsTwoFactorEnabled,
                @Status, @FailedLoginAttempts, @LockoutEndUtc, @LastLoginUtc,
                @LastPasswordChangeUtc, @MustChangePassword,
                @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
            )",
            new
            {
                user.Id,
                Username = user.Email.Split('@')[0],
                user.Email,
                user.NormalizedEmail,
                user.PasswordHash,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.PreferredLanguage,
                user.TimeZone,
                IsEmailConfirmed = user.EmailConfirmed,
                IsPhoneConfirmed = user.PhoneConfirmed,
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                Status = (int)user.Status,
                user.FailedLoginAttempts,
                LockoutEndUtc = user.LockoutEnd,
                LastLoginUtc = user.LastLoginAt,
                LastPasswordChangeUtc = user.PasswordChangedAt,
                user.MustChangePassword,
                user.CreatedAt,
                user.CreatedBy,
                user.ModifiedAt,
                user.ModifiedBy
            });

        return user;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
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
                [PreferredLanguage] = @PreferredLanguage,
                [TimeZone] = @TimeZone,
                [ModifiedAt] = @ModifiedAt,
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @Id",
            new
            {
                user.Id,
                user.Email,
                user.NormalizedEmail,
                user.PasswordHash,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                Status = (int)user.Status,
                IsEmailConfirmed = user.EmailConfirmed,
                IsPhoneConfirmed = user.PhoneConfirmed,
                IsTwoFactorEnabled = user.TwoFactorEnabled,
                user.FailedLoginAttempts,
                LockoutEndUtc = user.LockoutEnd,
                LastLoginUtc = user.LastLoginAt,
                LastPasswordChangeUtc = user.PasswordChangedAt,
                user.MustChangePassword,
                user.PreferredLanguage,
                user.TimeZone,
                user.ModifiedAt,
                user.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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
    public async Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var offset = (pageNumber - 1) * pageSize;
        var searchPattern = string.IsNullOrEmpty(searchTerm) ? null : $"%{searchTerm}%";

        var sql = @"
            SELECT COUNT(1) FROM [dbo].[Users]
            WHERE [IsDeleted] = 0
              AND (@SearchPattern IS NULL OR
                   [Email] LIKE @SearchPattern OR
                   [FirstName] LIKE @SearchPattern OR
                   [LastName] LIKE @SearchPattern);

            SELECT
                [Id], [Username], [Email], [NormalizedEmail], [PasswordHash],
                [FirstName], [LastName], [FullName] AS [DisplayName], [PhoneNumber],
                [PreferredLanguage], [TimeZone],
                [IsEmailConfirmed] AS [EmailConfirmed],
                [IsPhoneConfirmed] AS [PhoneConfirmed],
                [IsTwoFactorEnabled] AS [TwoFactorEnabled],
                [Status], [FailedLoginAttempts],
                [LockoutEndUtc] AS [LockoutEnd],
                [LastLoginUtc] AS [LastLoginAt],
                [LastPasswordChangeUtc] AS [PasswordChangedAt],
                [MustChangePassword],
                [CreatedAt], [CreatedBy], [ModifiedAt], [ModifiedBy]
            FROM [dbo].[Users]
            WHERE [IsDeleted] = 0
              AND (@SearchPattern IS NULL OR
                   [Email] LIKE @SearchPattern OR
                   [FirstName] LIKE @SearchPattern OR
                   [LastName] LIKE @SearchPattern)
            ORDER BY [CreatedAt] DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await connection.QueryMultipleAsync(sql, new
        {
            SearchPattern = searchPattern,
            Offset = offset,
            PageSize = pageSize
        });

        var totalCount = await multi.ReadSingleAsync<int>();
        var users = (await multi.ReadAsync<UserDto>()).Select(dto => dto.ToUser()).ToList();

        return (users, totalCount);
    }

    /// <inheritdoc />
    public async Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [LastLoginUtc] = GETUTCDATE(),
                [FailedLoginAttempts] = 0,
                [LockoutEndUtc] = NULL,
                [ModifiedAt] = GETUTCDATE()
            WHERE [Id] = @UserId",
            new { UserId = userId });
    }

    /// <inheritdoc />
    public async Task RecordFailedLoginAsync(
        Guid userId,
        int maxAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken = default)
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
    public async Task UnlockAsync(Guid userId, Guid modifiedBy, CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
        public string? Metadata { get; init; }
        public bool IsSystemUser { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }

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
            ModifiedBy);
    }
}
