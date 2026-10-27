using System.Data;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth.Domain.ValueObjects;
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
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        IIdentifierHasher identifierHasher,
        IOptionsSnapshot<AccountDeletionSettings> accountDeletionSettings,
        IPerUserCryptoService perUserCrypto,
        IOtpHasher otpHasher)
    {
        _connectionFactory = connectionFactory;
        _passwordSettings = passwordSettings.Value;
        _identifierHasher = identifierHasher;
        _accountDeletionSettings = accountDeletionSettings.Value;
        _perUserCrypto = perUserCrypto;
        _otpHasher = otpHasher;
    }

    // Only for CreateVerifiedAsync: the registration code is re-checked under
    // the pending row's lock, inside the same transaction that inserts the
    // account, and that check cannot live anywhere but here.
    private readonly IOtpHasher _otpHasher;

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
    public async Task<UserNotificationIdentity?> GetNotificationIdentityByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Three columns and no IsDeleted predicate, on purpose: the caller
        // classifies an address for a registration attempt, and a Users row in
        // any state — active, locked, soft-deleted, in its deletion grace —
        // means the address cannot become a new account. No phone decryption
        // and no aggregate hydration: this runs on every registration start,
        // free address or not, and must cost the same for both.
        return await connection.QueryFirstOrDefaultAsync<UserNotificationIdentity>(@"
            SELECT [Id], [FullName] AS [DisplayName], [PreferredLanguage], [IsDeleted]
            FROM [dbo].[Users]
            WHERE [NormalizedEmail] = @NormalizedEmail",
            new { NormalizedEmail = email.Trim().ToUpperInvariant() });
    }

    /// <inheritdoc />
    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await InsertUserAsync(connection, user, transaction: null);

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
    public Task<VerifiedUserCreationOutcome> CreateVerifiedAsync(
        User user, Guid pendingRegistrationId, string otp, CancellationToken cancellationToken)
        => CreateVerifiedAsync(user, pendingRegistrationId, otp, retryAfterDeadlock: true, cancellationToken);

    private async Task<VerifiedUserCreationOutcome> CreateVerifiedAsync(
        User user, Guid pendingRegistrationId, string otp, bool retryAfterDeadlock, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateVerifiedOnceAsync(user, pendingRegistrationId, otp, cancellationToken);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 && retryAfterDeadlock)
        {
            // This transaction reaches the pending row through its clustered
            // key and then, when it stamps ConsumedAt, needs the row's entries
            // in the two filtered indexes; the check and start steps reach the
            // same row through those indexes first. A check or a start landing
            // on this row in the same few milliseconds — a double submit — can
            // therefore deadlock with this one, and SQL Server kills one side.
            // The victim's transaction is already rolled back; run it once more
            // on a fresh connection. If the other side consumed or rotated the
            // row in between, the re-run answers CodeRejected on its own.
            return await CreateVerifiedAsync(user, pendingRegistrationId, otp, retryAfterDeadlock: false, cancellationToken);
        }
    }

    private async Task<VerifiedUserCreationOutcome> CreateVerifiedOnceAsync(
        User user, Guid pendingRegistrationId, string otp, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // The pending row under its lock, alive: unconsumed and unexpired by
        // the application clock, the same predicate the check step reads by.
        // Held from here to the commit, so nothing can rotate or consume the
        // row between this check and the account row that depends on it.
        var pending = await connection.QuerySingleOrDefaultAsync<PendingCodeRow>(@"
            SELECT [Id], [OtpHash]
            FROM [dbo].[PendingRegistrations] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @Id AND [ConsumedAt] IS NULL AND [ExpiresAt] > @Now",
            new { Id = pendingRegistrationId, Now = DateTime.UtcNow },
            transaction);

        if (pending is null)
        {
            // Gone, expired, or consumed. Consumed is worth telling apart: it
            // means an account exists for the address already — this very
            // form submitted twice, or another door winning the race — and
            // the caller, who has already presented the right code, should be
            // sent to sign in rather than back to a code that is spent.
            var consumed = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM [dbo].[PendingRegistrations]
                WHERE [Id] = @Id AND [ConsumedAt] IS NOT NULL",
                new { Id = pendingRegistrationId },
                transaction);

            transaction.Rollback();
            return consumed > 0
                ? VerifiedUserCreationOutcome.DuplicateEmail
                : VerifiedUserCreationOutcome.CodeRejected;
        }

        // A mismatch here is not a wrong guess — the caller's guess was
        // already charged and accepted by the check step under this same
        // lock — it means the row was rotated under our feet. No attempt is
        // spent and nothing is written.
        if (!_otpHasher.Verify(PendingRegistration.OtpScopeFor(pendingRegistrationId), otp, pending.OtpHash))
        {
            transaction.Rollback();
            return VerifiedUserCreationOutcome.CodeRejected;
        }

        try
        {
            await InsertUserAsync(connection, user, transaction);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Another door — Google, an invitation, an administrator — wrote
            // the address first. The account exists; the caller can sign in
            // through it. The pending row is left alone: the consumer that
            // door ran will have stamped it, or will.
            transaction.Rollback();
            return VerifiedUserCreationOutcome.DuplicateEmail;
        }

        // ConsumedAt means one thing only: a Users row now exists for the
        // address. It is stamped in the same transaction as that row.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PendingRegistrations]
            SET [ConsumedAt] = GETUTCDATE()
            WHERE [Id] = @Id AND [ConsumedAt] IS NULL",
            new { Id = pendingRegistrationId },
            transaction);

        transaction.Commit();
        return VerifiedUserCreationOutcome.Created;
    }

    private sealed class PendingCodeRow
    {
        public Guid Id { get; init; }
        public string OtpHash { get; init; } = string.Empty;
    }

    /// <summary>
    /// The one INSERT into Users in the application. Every code path that
    /// brings an account into existence must issue this statement and no other
    /// (the two seeded accounts are written by Script.PostDeployment.sql and
    /// keep their local-part usernames), so the column list, the parameter
    /// shape and the identifier rules live here once. The
    /// transaction is optional: <see cref="CreateAsync"/> runs it alone, while a
    /// caller that has to insert the row inside its own transaction (a verified
    /// registration consuming its pending row in the same commit) passes the
    /// transaction and gets the identical statement.
    /// </summary>
    private static Task InsertUserAsync(IDbConnection connection, User user, IDbTransaction? transaction)
    {
        return connection.ExecuteAsync(InsertUserSql, InsertUserParameters(user), transaction);
    }

    private const string InsertUserSql = @"
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
            )";

    private static object InsertUserParameters(User user) => new
    {
        user.Id,
        // The sign-in identifier is the full address. It used to be the local
        // part — everything before '@' — and UQ_Users_Username made that a live
        // fault: jane@a.example and jane@b.example collided on "jane", and the
        // second registration died as an unhandled unique violation. Nothing
        // reads Username as a local part (the search matches it with LIKE, the
        // purge by equality against the stored value), so rows written before
        // this keep their shape and no backfill is needed.
        Username = user.Email.Value,
        Email = user.Email.Value,
        user.NormalizedEmail,
        user.PasswordHash,
        user.FirstName,
        user.LastName,
        // Written after the row exists: the ciphertext needs the per-user DEK,
        // whose row has a foreign key to this one.
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
    };

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var encryptedPhone = string.IsNullOrEmpty(user.PhoneNumber?.Value)
            ? null
            : await _perUserCrypto.EncryptAsync(
                user.Id, user.PhoneNumber.Value, EncryptedFieldPurpose.UserPhoneNumber, cancellationToken);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // IsTwoFactorEnabled is deliberately absent. This write is built from a read,
        // and its callers (an avatar adopted at sign-in, a profile edit) hold copies
        // that can be minutes old; writing the flag back from one switched two-factor
        // off behind the user's back. The flag changes only with the factor row, in
        // the transactions of ITwoFactorStateStore.
        //
        // Status, FailedLoginAttempts and LockoutEndUtc are absent for the same
        // reason. A profile edit loaded minutes before a burst of wrong passwords
        // wrote the old count and the old lock back, undoing the lock; an avatar
        // adopted at sign-in could lift one. The lock columns change only through
        // the lock statements below, each of which decides from the row itself.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[Users] SET
                [Email] = @Email,
                [NormalizedEmail] = @NormalizedEmail,
                [PasswordHash] = @PasswordHash,
                [FirstName] = @FirstName,
                [LastName] = @LastName,
                [PhoneNumber] = @PhoneNumber,
                [IsEmailConfirmed] = @IsEmailConfirmed,
                [IsPhoneConfirmed] = @IsPhoneConfirmed,
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
                IsEmailConfirmed = user.EmailConfirmed,
                IsPhoneConfirmed = user.PhoneConfirmed,
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

        await SoftDeleteUserAsync(connection, null, id, cancellationToken);
    }

    private static Task SoftDeleteUserAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid id, CancellationToken cancellationToken)
    {
        // Soft delete by setting IsDeleted flag
        return connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE [dbo].[Users] SET
                [IsDeleted] = 1,
                [DeletedAt] = GETUTCDATE(),
                [DeletedBy] = @Id
            WHERE [Id] = @Id",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
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

        // Staged destruction. Every table below either references Users
        // through a non-cascading foreign key, carries a loose user reference
        // (AuditLogs, NotificationOutbox, RevokedTokens), or is bound to the
        // account by IDENTIFIER rather than by id (OrganizationInvitations.Email,
        // NotificationOutbox.Recipient, LoginAttempts.Username) — that last
        // class has no foreign key to drift-check, so it is enumerated
        // explicitly and guarded by UserHardDeleteSqlTests. Rows the
        // user owns are deleted; the audit/login history is anonymized in
        // place; actor references on records that belong to other entities are
        // reattributed to the system account so those rows keep resolving.
        // AccountDeletionRequests rows are retained untouched as destruction
        // evidence. UserHardDeleteSqlTests guards this list against schema drift.
        await connection.ExecuteAsync(@"
            -- Destruction tombstone (idempotent MERGE): the identifier
            -- reservation and the restore re-apply anchor. Written before
            -- anything is destroyed so a mid-purge failure never loses it.
            -- The row is retained for IdentifierReservationDays, then swept —
            -- a keyed digest of an address is pseudonymous, not anonymous.
            MERGE [dbo].[AccountDeletionTombstones] WITH (HOLDLOCK) AS [target]
            USING (SELECT @EmailHash AS [EmailHash]) AS [source]
            ON [target].[EmailHash] = [source].[EmailHash]
            WHEN NOT MATCHED THEN
                INSERT ([EmailHash], [DeletedAtUtc], [PolicyVersion], [KeyVersion])
                VALUES (@EmailHash, GETUTCDATE(), @PolicyVersion, @KeyVersion);

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
            -- Codes emailed before a first second factor: before the factor row,
            -- the order the bind itself takes them in.
            DELETE FROM [dbo].[TwoFactorBindCodes] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @Id;
            -- Step-up confirmations the admin raised against the secret store.
            -- The rotations they authorized survive in AuditLogs; the codes
            -- themselves are spent credentials with no evidentiary value.
            DELETE FROM [dbo].[SecretOperationChallenges] WHERE [RequestedBy] = @Id;
            DELETE FROM [dbo].[RevokedTokens] WHERE [RevocationKey] = CONVERT(NVARCHAR(200), @Id);

            -- Platform-level assignments
            DELETE FROM [dbo].[UserRoles] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[UserPermissions] WHERE [UserId] = @Id;

            -- Invitations to individual applications. Deleted rather than kept
            -- as a revoked row: the account is being destroyed, and a surviving
            -- row would name a user that no longer exists.
            DELETE FROM [dbo].[ApplicationUserAccess] WHERE [UserId] = @Id;

            -- Organization memberships and artifacts the user authored. The
            -- membership first, then its grants: the order member removal and
            -- application-role assignment take these rows in (OI-231), so a
            -- purge racing either one waits instead of deadlocking.
            DELETE FROM [dbo].[OrganizationUsers] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[OrganizationUserRoles] WHERE [UserId] = @Id;
            DELETE FROM [dbo].[OrganizationUserPermissions] WHERE [UserId] = @Id;
            -- Invitations the user authored, AND every invitation addressed to
            -- the destroyed identifier. The second predicate is not optional:
            -- the row stores the address in clear text and the accept path binds
            -- on that string, not on a user id, so an untouched Pending row is
            -- both surviving PII and a membership that the next holder of the
            -- address would inherit the moment the reservation lapses.
            DELETE FROM [dbo].[OrganizationInvitations]
            WHERE [InvitedBy] = @Id OR [Email] = @Email;
            DELETE FROM [dbo].[OwnershipTransferCodes] WHERE [TargetUserId] = @Id OR [InitiatedBy] = @Id;
            -- Self-registration attempts bound to the address in clear text.
            -- Consumed or not: a consumed row is the record of how this account
            -- came to exist, and an unconsumed one is a code the next holder of
            -- the address must not be able to redeem. Deleted here, inside the
            -- identifier-bound block and before the Users row goes. Lock order,
            -- stated plainly: this purge holds the Users row first and takes
            -- the pending row second; the completion step (CreateVerifiedAsync)
            -- holds the pending row first and inserts into Users second. The
            -- orders are OPPOSITE, and the two can only meet on a code minted
            -- for a soft-deleted address, which is never mailed — so the
            -- inversion is unreachable without a one-in-a-million guess, and
            -- if it were reached SQL Server would kill one side (1205), which
            -- the completion step retries once. Keep it that way: never read
            -- Users inside CreateVerifiedAsync's transaction, and never move
            -- this delete after the Users delete below.
            DELETE FROM [dbo].[PendingRegistrations] WHERE [Email] = @Email;

            -- Mail addressed to the user. RecipientUserId is deliberately a soft
            -- reference and is NULL for everything queued before the account
            -- existed (invitation, e-mail verification), so the address is
            -- matched too: Recipient, Subject and BodyHtml hold rendered PII.
            DELETE FROM [dbo].[NotificationOutbox]
            WHERE [RecipientUserId] = @Id OR [Recipient] = @Email;

            -- Client display state (table column layouts); no audit value
            DELETE FROM [dbo].[UserUiPreferences] WHERE [UserId] = @Id;

            -- Recognised devices: a recognition aid, not part of the security
            -- record (the sign-ins themselves survive in LoginAttempts)
            DELETE FROM [dbo].[UserKnownDevices] WHERE [UserId] = @Id;

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
            -- Matched by identifier as well as by id. Username holds the
            -- ATTEMPTED identifier, so every attempt that never resolved to an
            -- account — a wrong password before the first sign-in, credential
            -- stuffing against the address — carries the e-mail in clear text
            -- under a NULL UserId and would otherwise survive the purge.
            -- Username is the one identifier column storing raw user input
            -- rather than a normalized form, so this predicate (alone in the
            -- purge) relies on the database's case-insensitive collation.
            -- IpAddress is NOT NULL, so it is overwritten with a sentinel rather
            -- than nulled: the published policy promises these rows are
            -- de-identified immediately on deletion, and leaving the IP and
            -- user-agent in place kept identifying data of an erased account
            -- alive for a further LoginAttemptRetentionDays. The AuditLogs
            -- statement above already strips both.
            UPDATE [dbo].[LoginAttempts]
            SET [UserId] = NULL, [Username] = N'[deleted]',
                [IpAddress] = N'0.0.0.0', [UserAgent] = NULL
            WHERE [UserId] = @Id OR [Username] = @Email OR [Username] = @Username;

            -- Actor references on surviving records of other entities
            UPDATE [dbo].[OrganizationApplications] SET [EnabledBy] = @SystemUserId WHERE [EnabledBy] = @Id;
            UPDATE [dbo].[OrganizationUsers] SET [InvitedBy] = @SystemUserId WHERE [InvitedBy] = @Id;
            UPDATE [dbo].[OrganizationUserRoles] SET [AssignedBy] = @SystemUserId WHERE [AssignedBy] = @Id;
            UPDATE [dbo].[OrganizationUserPermissions] SET [GrantedBy] = @SystemUserId WHERE [GrantedBy] = @Id;
            UPDATE [dbo].[OrganizationInvitations] SET [AcceptedByUserId] = NULL WHERE [AcceptedByUserId] = @Id;
            -- Invitations OTHER users still hold, issued by the departing
            -- administrator: the invitation belongs to its invitee, so only the
            -- actor reference moves. RevokedBy has no foreign key and is left as
            -- historical detail on rows already withdrawn.
            UPDATE [dbo].[ApplicationUserAccess] SET [GrantedBy] = @SystemUserId WHERE [GrantedBy] = @Id;

            -- The account row last; IsDeleted = 1 is the final in-database guard
            DELETE FROM [dbo].[Users] WHERE [Id] = @Id AND [IsDeleted] = 1;",
            new
            {
                Id = id,
                SystemUserId = WellKnownUserIds.System,
                EmailHash = emailHash,
                KeyVersion = _accountDeletionSettings.IdentifierKeyVersion,
                // Email.Value is stored lower-invariant and NormalizedEmail is its
                // upper-invariant form, so this reproduces exactly what the
                // identifier-bearing columns hold.
                Email = identifiers.NormalizedEmail.ToLowerInvariant(),
                Username = identifiers.Username,
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
              AND {UserSearchSql.Matches()};

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
              AND {UserSearchSql.Matches()}
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

    // ── The lock columns ────────────────────────────────────────────────────
    //
    // Status, FailedLoginAttempts and LockoutEndUtc are written by these five
    // statements and by the insert, and by nothing else (UpdateAsync leaves them
    // out; UserRepositoryLockColumnsSourceGuardTests holds that). Each statement
    // decides from the row it is changing, never from what an earlier read saw,
    // and reports what it did through OUTPUT, so a concurrent request can neither
    // undo a lock nor make a signal lie. The kinds the CASE arms encode are the
    // table of AccountLockState.Kind:
    //   administrator: Status 3, count 0, no expiry or one ahead;
    //   timed:         Status 3, count > 0, expiry ahead;
    //   hard:          Status 3, count > 0, no expiry;
    //   lapsed:        Status 3, expiry passed.

    /// <summary>The before and after of the lock columns, from the statement's own OUTPUT.</summary>
    private const string LockTransitionOutput = @"
            OUTPUT deleted.[Status] AS BeforeStatus,
                   deleted.[FailedLoginAttempts] AS BeforeFailedAttempts,
                   deleted.[LockoutEndUtc] AS BeforeLockoutEndUtc,
                   inserted.[Status] AS AfterStatus,
                   inserted.[FailedLoginAttempts] AS AfterFailedAttempts,
                   inserted.[LockoutEndUtc] AS AfterLockoutEndUtc";

    /// <summary>
    /// The one SET clause that lifts an automatic lock and starts a new count,
    /// shared by a password-proving sign-in and by a completed reset or change.
    /// "Automatic or lapsed" is a count above zero, or an expiry already passed;
    /// a live administrator's lock (count 0, no expiry or one ahead) matches neither
    /// and keeps its status and expiry. Every expression reads the row as it was
    /// before the statement.
    /// </summary>
    private const string AutomaticLockClearSetClause = @"
                [Status] = CASE
                    WHEN [Status] = 3 AND ([FailedLoginAttempts] > 0 OR [LockoutEndUtc] <= SYSUTCDATETIME())
                    THEN 1 ELSE [Status] END,
                [LockoutEndUtc] = CASE
                    WHEN [Status] = 3 AND ([FailedLoginAttempts] > 0 OR [LockoutEndUtc] <= SYSUTCDATETIME())
                    THEN NULL ELSE [LockoutEndUtc] END,
                [FailedLoginAttempts] = 0";

    internal const string RecordSuccessfulPasswordLoginSql = @"
            UPDATE [dbo].[Users] SET
                [LastLoginUtc] = SYSUTCDATETIME(),
                [LastLoginIp] = @IpAddress," + AutomaticLockClearSetClause + @",
                [ModifiedAt] = SYSUTCDATETIME()" + LockTransitionOutput + @"
            WHERE [Id] = @UserId";

    internal const string RecordSuccessfulLoginSql = @"
            UPDATE [dbo].[Users] SET
                [LastLoginUtc] = SYSUTCDATETIME(),
                [LastLoginIp] = @IpAddress,
                [ModifiedAt] = SYSUTCDATETIME()" + LockTransitionOutput + @"
            WHERE [Id] = @UserId";

    internal const string ClearAutomaticLockSql = @"
            UPDATE [dbo].[Users] SET" + AutomaticLockClearSetClause + @",
                [ModifiedAt] = SYSUTCDATETIME()" + LockTransitionOutput + @"
            WHERE [Id] = @UserId AND [IsDeleted] = 0";

    // The precedence is the rule, so the arms are in this order on purpose:
    //   1. the hard limit reached (when one applies), or the row already hard —
    //      a hard lock has no expiry and a later failure never gives it one;
    //   2. else every @Max-th consecutive failure starts a timed lock — Max per
    //      window, the count kept, so it climbs toward the hard limit;
    //   3. else a lapsed lock is repaired to active;
    //   4. else the row keeps its status and expiry (a familiar source's failure
    //      inside a timed lock counts without extending it).
    // The WHERE never counts against a deleted, inactive or pending row, nor
    // against a live administrator's lock: a raised count would make that lock
    // read as automatic, and a familiar device may pass an automatic one.
    internal const string RecordFailedLoginSql = @"
            UPDATE [dbo].[Users] SET
                [FailedLoginAttempts] = [FailedLoginAttempts] + 1,
                [Status] = CASE
                    WHEN (@Hard IS NOT NULL AND [FailedLoginAttempts] + 1 >= @Hard)
                         OR ([Status] = 3 AND [LockoutEndUtc] IS NULL) THEN 3
                    WHEN ([FailedLoginAttempts] + 1) % @Max = 0 THEN 3
                    WHEN [Status] = 3 AND [LockoutEndUtc] <= SYSUTCDATETIME() THEN 1
                    ELSE [Status] END,
                [LockoutEndUtc] = CASE
                    WHEN (@Hard IS NOT NULL AND [FailedLoginAttempts] + 1 >= @Hard)
                         OR ([Status] = 3 AND [LockoutEndUtc] IS NULL) THEN NULL
                    WHEN ([FailedLoginAttempts] + 1) % @Max = 0 THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
                    WHEN [Status] = 3 AND [LockoutEndUtc] <= SYSUTCDATETIME() THEN NULL
                    ELSE [LockoutEndUtc] END,
                [ModifiedAt] = SYSUTCDATETIME()" + LockTransitionOutput + @"
            WHERE [Id] = @UserId
              AND [IsDeleted] = 0
              AND [Status] IN (1, 3)
              AND NOT ([Status] = 3 AND [FailedLoginAttempts] = 0
                       AND ([LockoutEndUtc] IS NULL OR [LockoutEndUtc] > SYSUTCDATETIME()))";

    internal const string RepairLapsedLockSql = @"
            UPDATE [dbo].[Users] SET
                [Status] = 1,
                [LockoutEndUtc] = NULL,
                [FailedLoginAttempts] = CASE WHEN @ResetCount = 1 THEN 0 ELSE [FailedLoginAttempts] END,
                [ModifiedAt] = SYSUTCDATETIME()" + LockTransitionOutput + @"
            WHERE [Id] = @UserId
              AND [Status] = 3
              AND [LockoutEndUtc] <= SYSUTCDATETIME()";

    internal const string SetAccountStatusSql = @"
            UPDATE [dbo].[Users] SET
                [Status] = @Status,
                [FailedLoginAttempts] = CASE WHEN @ResetsLock = 1 THEN 0 ELSE [FailedLoginAttempts] END,
                [LockoutEndUtc] = CASE WHEN @ResetsLock = 1 THEN @LockoutEndUtc ELSE [LockoutEndUtc] END,
                [ModifiedAt] = SYSUTCDATETIME(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @UserId AND [IsDeleted] = 0";

    /// <inheritdoc />
    public async Task<AccountLockTransition?> RecordSuccessfulLoginAsync(
        Guid userId, string? ipAddress, bool passwordProven, CancellationToken cancellationToken)
    {
        // Only a sign-in that proved the password lifts an automatic lock and
        // starts a new count (owner decision G8). Any other sign-in — a provider,
        // an emailed code, a token exchange — writes the last-login fields and
        // nothing else, so it can neither lift a lock nor leave a Locked row with
        // its expiry cleared (the indefinite lock the old statement produced).
        return await ExecuteLockStatementAsync(
            passwordProven ? RecordSuccessfulPasswordLoginSql : RecordSuccessfulLoginSql,
            new { UserId = userId, IpAddress = ipAddress },
            userId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AccountLockTransition?> RecordFailedLoginAsync(
        Guid userId,
        int? hardLimit,
        int lockThreshold,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lockThreshold, 1);

        return ExecuteLockStatementAsync(
            RecordFailedLoginSql,
            new
            {
                UserId = userId,
                Hard = hardLimit,
                Max = lockThreshold,
                LockoutMinutes = (int)lockoutDuration.TotalMinutes
            },
            userId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AccountLockTransition?> ClearAutomaticLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        return ExecuteLockStatementAsync(ClearAutomaticLockSql, new { UserId = userId }, userId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AccountLockTransition?> RepairLapsedLockAsync(
        Guid userId, bool resetFailureCount, CancellationToken cancellationToken)
    {
        return ExecuteLockStatementAsync(
            RepairLapsedLockSql,
            new { UserId = userId, ResetCount = resetFailureCount },
            userId,
            cancellationToken);
    }

    internal const string GetLockStateSql = @"
            SELECT [Status], [FailedLoginAttempts] AS FailedAttempts, [LockoutEndUtc]
            FROM [dbo].[Users]
            WHERE [Id] = @UserId AND [IsDeleted] = 0";

    /// <inheritdoc />
    public async Task<AccountLockState?> GetLockStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<LockStateRow>(
            new CommandDefinition(GetLockStateSql, new { UserId = userId }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new AccountLockState((UserStatus)row.Status, row.FailedAttempts, row.LockoutEndUtc);
    }

    private sealed record LockStateRow
    {
        public int Status { get; init; }
        public int FailedAttempts { get; init; }
        public DateTime? LockoutEndUtc { get; init; }
    }

    /// <inheritdoc />
    public async Task SetAccountStatusAsync(
        Guid userId, AccountStatusChange change, Guid modifiedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await UpdateAccountStatusAsync(connection, null, userId, change, modifiedBy, cancellationToken);
    }

    private static Task UpdateAccountStatusAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid userId,
        AccountStatusChange change,
        Guid modifiedBy,
        CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(new CommandDefinition(
            SetAccountStatusSql,
            new
            {
                UserId = userId,
                Status = (int)change.Status,
                change.ResetsLock,
                change.LockoutEndUtc,
                ModifiedBy = modifiedBy
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The one lock every <see cref="IAccountAuthorityChange"/> takes, owned by its
    /// transaction: whichever of two such changes asks second waits until the first
    /// has committed or rolled back, then reads what it left. A lock not granted
    /// within the timeout (or a deadlock) is a fault, never a decision: it throws,
    /// and nothing has been written.
    /// </summary>
    internal const string AccountAuthorityLockSql = @"
            DECLARE @Result INT;
            EXEC @Result = sp_getapplock
                @Resource = N'Auth.FullAdministrators',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @Result < 0
                THROW 51000, N'The account-authority lock was not granted.', 1;";

    /// <inheritdoc />
    public async Task<IAccountAuthorityChange> BeginAuthorityChangeAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        IDbTransaction? transaction = null;
        try
        {
            transaction = connection.BeginTransaction();

            // The lock first, then the read: a grant read before the lock could
            // already be stale when the change is decided on it.
            await connection.ExecuteAsync(new CommandDefinition(
                AccountAuthorityLockSql, transaction: transaction, cancellationToken: cancellationToken));
            var grants = await PermissionRepository.ReadFullAdministratorGrantsAsync(
                connection, transaction, cancellationToken);

            return new AccountAuthorityChange(connection, transaction, grants);
        }
        catch
        {
            transaction?.Dispose();
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The writes of an <see cref="IAccountAuthorityChange"/>: the same statements as
    /// the repository's own methods, on the connection and transaction that hold the
    /// lock.
    /// </summary>
    private sealed class AccountAuthorityChange(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyList<FullAdministratorGrant> grants) : IAccountAuthorityChange
    {
        public IReadOnlyList<FullAdministratorGrant> FullAdministratorGrants => grants;

        public Task<bool> RemoveRoleAsync(Guid userId, Guid roleId, Guid? applicationId, CancellationToken cancellationToken) =>
            DeleteUserRoleAsync(connection, transaction, userId, roleId, applicationId, cancellationToken);

        public Task<bool> RevokePermissionAsync(Guid userId, Guid permissionId, Guid? applicationId, CancellationToken cancellationToken) =>
            DeleteUserPermissionAsync(connection, transaction, userId, permissionId, applicationId, cancellationToken);

        public Task<bool> RevokeFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken) =>
            PermissionRepository.DeleteRolePermissionAsync(connection, transaction, roleId, permissionId, cancellationToken);

        public Task SetAccountStatusAsync(
            Guid userId, AccountStatusChange change, Guid modifiedBy, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(change);
            return UpdateAccountStatusAsync(connection, transaction, userId, change, modifiedBy, cancellationToken);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            SoftDeleteUserAsync(connection, transaction, id, cancellationToken);

        public void Commit() => transaction.Commit();

        public ValueTask DisposeAsync()
        {
            // An uncommitted transaction rolls back here, which also releases the lock.
            // The connection goes back to the pool even when that throws (a broken
            // connection): the server ends the transaction with the session.
            try
            {
                transaction.Dispose();
            }
            finally
            {
                connection.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }

    private async Task<AccountLockTransition?> ExecuteLockStatementAsync(
        string sql, object parameters, Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<LockTransitionRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return row?.ToTransition(userId);
    }

    private sealed record LockTransitionRow
    {
        public int BeforeStatus { get; init; }
        public int BeforeFailedAttempts { get; init; }
        public DateTime? BeforeLockoutEndUtc { get; init; }
        public int AfterStatus { get; init; }
        public int AfterFailedAttempts { get; init; }
        public DateTime? AfterLockoutEndUtc { get; init; }

        public AccountLockTransition ToTransition(Guid userId) => new(
            userId,
            new AccountLockState((UserStatus)BeforeStatus, BeforeFailedAttempts, BeforeLockoutEndUtc),
            new AccountLockState((UserStatus)AfterStatus, AfterFailedAttempts, AfterLockoutEndUtc));
    }

    // The same password under new hashing parameters: the hash alone. Never the
    // change stamp, which the second factor's reset gate reads as the owner's own
    // password change, and never MustChangePassword, which an administrator set.
    // Only over the hash that was verified: a reset that landed during the sign-in's
    // two Argon2 runs changed it, so this matches nothing and the reset stands.
    internal const string RehashPasswordSql = @"
            UPDATE [dbo].[Users] SET
                [PasswordHash] = @PasswordHash
            WHERE [Id] = @UserId
              AND [PasswordHash] = @VerifiedHash";

    /// <inheritdoc />
    public async Task RehashPasswordAsync(
        Guid userId, string verifiedHash, string passwordHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // No row changed is not an error: the password moved on, and the new one
        // is hashed under the current parameters already.
        await connection.ExecuteAsync(new CommandDefinition(
            RehashPasswordSql,
            new { UserId = userId, VerifiedHash = verifiedHash, PasswordHash = passwordHash },
            cancellationToken: cancellationToken));
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

    // The first confirmation by a sign-in code (OI-215, D-215-5 (a)): the address and
    // the retirement of a password set before it was proven are one statement, so no
    // reader sees a confirmed address that still carries that password. Only over the
    // state the sign-in read: still unconfirmed, still the same hash (or none). The
    // change stamp is left alone: the mailbox was proven, never a password.
    internal const string ConfirmEmailBySignInCodeSql = @"
            UPDATE [dbo].[Users] SET
                [IsEmailConfirmed] = 1,
                [PasswordHash] = NULL,
                [MustChangePassword] = 0,
                [PasswordExpiresUtc] = NULL,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @UserId
            WHERE [Id] = @UserId
              AND [IsEmailConfirmed] = 0
              AND (([PasswordHash] IS NULL AND @ReadPasswordHash IS NULL) OR [PasswordHash] = @ReadPasswordHash)";

    /// <inheritdoc />
    public async Task<bool> ConfirmEmailBySignInCodeAsync(
        Guid userId, string? readPasswordHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            ConfirmEmailBySignInCodeSql,
            new { UserId = userId, ReadPasswordHash = readPasswordHash },
            cancellationToken: cancellationToken)) == 1;
    }

    // The owner's removal (OI-215, D-215-3): only over the hash the handler read and
    // only on a confirmed address, with the change stamp a password change writes.
    internal const string RemovePasswordSql = @"
            UPDATE [dbo].[Users] SET
                [PasswordHash] = NULL,
                [LastPasswordChangeUtc] = GETUTCDATE(),
                [MustChangePassword] = 0,
                [PasswordExpiresUtc] = NULL,
                [ModifiedAt] = GETUTCDATE(),
                [ModifiedBy] = @ModifiedBy
            WHERE [Id] = @UserId
              AND [IsEmailConfirmed] = 1
              AND [PasswordHash] = @ReadPasswordHash";

    /// <inheritdoc />
    public async Task<bool> RemovePasswordAsync(
        Guid userId, string readPasswordHash, Guid modifiedBy, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            RemovePasswordSql,
            new { UserId = userId, ReadPasswordHash = readPasswordHash, ModifiedBy = modifiedBy },
            transaction,
            cancellationToken: cancellationToken)) == 1;

        if (!removed)
        {
            transaction.Rollback();
            return false;
        }

        // The removed hash joins the history in the same transaction, as a change's
        // old hash does, so the history checks that read it still see it.
        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO [dbo].[PasswordHistory] ([Id], [UserId], [PasswordHash], [CreatedAt])
            VALUES (NEWID(), @UserId, @PasswordHash, GETUTCDATE())",
            new { UserId = userId, PasswordHash = readPasswordHash },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
    }

    /// <inheritdoc />
    public async Task<int> CountEmailCodeOnlyAccountsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Live accounts with no password and no provider: the emailed code is their
        // only way in. An account waiting for deletion is not counted; it signs in by
        // nothing until it is recovered. Nor is the system account, which signs in
        // by no means at all.
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
            SELECT COUNT(*)
            FROM [dbo].[Users] u
            WHERE u.[PasswordHash] IS NULL
              AND u.[IsDeleted] = 0
              AND u.[Id] <> @SystemUserId
              AND NOT EXISTS (SELECT 1 FROM [dbo].[UserExternalLogins] el WHERE el.[UserId] = u.[Id])",
            new { SystemUserId = WellKnownUserIds.System },
            cancellationToken: cancellationToken));
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

    /// <summary>
    /// The one removal of a role assignment, reached only through
    /// <see cref="IAccountAuthorityChange.RemoveRoleAsync"/> on the change's connection
    /// and transaction.
    /// </summary>
    private static async Task<bool> DeleteUserRoleAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid userId,
        Guid roleId,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
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

        // The row count is the answer, not a separate existence check: a check keyed
        // differently from this DELETE passed for an application-scoped assignment
        // that the platform-scoped DELETE then missed, and the removal was reported
        // and audited without having happened. UQ_UserRoles allows one row at most;
        // "> 0" rather than "== 1" so that a database missing it still reports the
        // removal it made instead of hiding it.
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                RoleId = roleId,
                ApplicationId = applicationId
            },
            transaction,
            cancellationToken: cancellationToken));

        return removed > 0;
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

        // The twin of RoleRepository.AssignToUserAsync. UQ_UserPermissions holds
        // neither IsActive nor ExpiresAt, so a lapsed or deactivated grant keeps its
        // row and a plain INSERT of the same grant violated the key (2627, a 500).
        // A serializable update-or-insert restores that row in place and protects
        // the key from a concurrent grant.
        var command = new CommandDefinition(@"
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            BEGIN TRY
                UPDATE [dbo].[UserPermissions] WITH (UPDLOCK, HOLDLOCK)
                SET [GrantedAt] = @GrantedAt,
                    [GrantedBy] = @GrantedBy,
                    [ExpiresAt] = @ExpiresAt,
                    [IsActive] = @IsActive
                WHERE [UserId] = @UserId
                  AND [PermissionId] = @PermissionId
                  AND ([ApplicationId] = @ApplicationId
                       OR ([ApplicationId] IS NULL AND @ApplicationId IS NULL));

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO [dbo].[UserPermissions] (
                        [Id], [UserId], [PermissionId], [ApplicationId], [GrantedAt], [GrantedBy], [ExpiresAt], [IsActive]
                    ) VALUES (
                        @Id, @UserId, @PermissionId, @ApplicationId, @GrantedAt, @GrantedBy, @ExpiresAt, @IsActive
                    );
                END;

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;",
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
            },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);

        return userPermission;
    }

    /// <summary>
    /// The one revocation of a direct grant, reached only through
    /// <see cref="IAccountAuthorityChange.RevokePermissionAsync"/> on the change's
    /// connection and transaction.
    /// </summary>
    private static async Task<bool> DeleteUserPermissionAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid userId,
        Guid permissionId,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
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

        // The row count is the answer, as in RemoveRoleAsync and for the same
        // reasons, "> 0" included. UQ_UserPermissions allows one row at most.
        var revoked = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                PermissionId = permissionId,
                ApplicationId = applicationId
            },
            transaction,
            cancellationToken: cancellationToken));

        return revoked > 0;
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
