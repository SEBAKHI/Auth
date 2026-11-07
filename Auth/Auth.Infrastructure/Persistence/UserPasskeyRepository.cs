using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the passkey repository (S20). Every change is one conditional
/// statement whose affected-row count is the decision; a credential id is compared as
/// bytes (VARBINARY), never as text under a case-insensitive collation (P7-R15).
/// </summary>
/// <remarks>
/// Without the passkey schema (code deployed before its DACPAC) every read answers "none"
/// and every write changes nothing: a statement naming the missing table would fail.
/// </remarks>
public sealed class UserPasskeyRepository : IUserPasskeyRepository
{
    /// <summary>The columns a passkey is read with, as one SELECT list.</summary>
    internal const string Columns = @"
                [Id], [UserId], [CredentialId], [PublicKey], [SignCount], [RpId],
                [IsUserVerified], [IsBackupEligible], [IsBackedUp], [Name], [CreatedAt],
                [LastUsedAt], [LastCounterRegressionAt], [RevokedAt], [RevokedReason], [RevokedBy]";

    /// <summary>The same columns as an <c>OUTPUT</c> list of the rows a statement changed.</summary>
    internal const string InsertedColumns = @"
                inserted.[Id], inserted.[UserId], inserted.[CredentialId], inserted.[PublicKey],
                inserted.[SignCount], inserted.[RpId], inserted.[IsUserVerified], inserted.[IsBackupEligible],
                inserted.[IsBackedUp], inserted.[Name], inserted.[CreatedAt], inserted.[LastUsedAt],
                inserted.[LastCounterRegressionAt], inserted.[RevokedAt], inserted.[RevokedReason], inserted.[RevokedBy]";

    // The live-passkey limit, the session and the insert in one statement. The count is read
    // under a range lock held to the end of the statement, so of two registrations that would
    // each be the eleventh, one is refused. The session that authorised the registration is
    // read under a lock its end must wait for (REVIEW 150 F1): a password change, a reset or a
    // lock ends the sessions before it revokes the recent passkeys, so an insert either commits
    // first, and that revocation catches it, or finds the session ended and stores nothing.
    // With email off another factor must remain (REVIEW-FIXES 150 F19): the app's row, or another
    // live passkey, read under a lock the app's switch-off waits for. The switch-off deletes that
    // row first and then reads the passkeys under a range lock, so of the two at the same moment
    // one sees the other: never a passkey as an account's only factor while email is off.
    internal const string InsertWithinLimitSql = @"
            INSERT INTO [dbo].[UserPasskeys]
                ([Id], [UserId], [CredentialId], [PublicKey], [SignCount], [RpId],
                 [IsUserVerified], [IsBackupEligible], [IsBackedUp], [Name], [CreatedAt])
            SELECT @Id, @UserId, @CredentialId, @PublicKey, @SignCount, @RpId,
                   @IsUserVerified, @IsBackupEligible, @IsBackedUp, @Name, @CreatedAt
            WHERE (SELECT COUNT(*) FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK)
                   WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL) < @MaxLive
              AND EXISTS (SELECT 1 FROM [dbo].[UserSessions] WITH (UPDLOCK, HOLDLOCK)
                          WHERE [Id] = @SessionId AND [UserId] = @UserId AND [EndedAt] IS NULL)
              AND (@RequireAnotherFactor = 0
                   OR EXISTS (SELECT 1 FROM [dbo].[TwoFactorAuth] WITH (UPDLOCK, HOLDLOCK)
                              WHERE [UserId] = @UserId AND [IsEnabled] = 1)
                   OR EXISTS (SELECT 1 FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK)
                              WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL
                                AND [IsUserVerified] = 1))";

    // Which of the insert's conditions failed, read in its transaction before the rollback: the
    // locks are still held, so the answer is the one the insert saw.
    internal const string SessionLiveSql = @"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserSessions]
                WHERE [Id] = @SessionId AND [UserId] = @UserId AND [EndedAt] IS NULL)
            THEN 1 ELSE 0 END AS BIT)";

    internal const string AnotherFactorSql = @"
            SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @UserId AND [IsEnabled] = 1)
                OR EXISTS (
                    SELECT 1 FROM [dbo].[UserPasskeys]
                    WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL AND [IsUserVerified] = 1)
            THEN 1 ELSE 0 END AS BIT)";

    // The handle is set once: only while the column is still empty. The read that follows
    // returns the value that won, whoever wrote it.
    internal const string EnsureUserHandleSql = @"
            UPDATE [dbo].[Users] SET [WebAuthnUserHandle] = @Candidate
            WHERE [Id] = @UserId AND [WebAuthnUserHandle] IS NULL;

            SELECT [WebAuthnUserHandle] FROM [dbo].[Users] WHERE [Id] = @UserId;";

    internal const string RevokeOneSql = @"
            UPDATE [dbo].[UserPasskeys] SET
                [RevokedAt] = SYSUTCDATETIME(),
                [RevokedReason] = @Reason,
                [RevokedBy] = @RevokedBy
            OUTPUT " + InsertedColumns + @"
            WHERE [Id] = @Id
              AND [UserId] = @UserId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL";

    // The same revocation with the floor of an administrator the policy binds (REVIEW 150 F4):
    // the live count is read under a range lock held to the end of the statement, so of two
    // removals that would each take one of the last two passkeys, one changes nothing.
    internal const string RevokeOneUnlessLastSql = RevokeOneSql + @"
              AND (SELECT COUNT(*) FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK)
                   WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL) > 1";

    // The reads a person or a policy makes: the caller's own live rows of the RP ID in force.
    internal const string LiveSql = @"
            SELECT" + Columns + @"
            FROM [dbo].[UserPasskeys]
            WHERE [UserId] = @UserId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL
            ORDER BY [CreatedAt], [Id]";

    internal const string LiveByIdSql = @"
            SELECT" + Columns + @"
            FROM [dbo].[UserPasskeys]
            WHERE [Id] = @Id
              AND [UserId] = @UserId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL";

    internal const string CountLiveSql = @"
            SELECT COUNT(*)
            FROM [dbo].[UserPasskeys]
            WHERE [UserId] = @UserId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL";

    internal const string RevokeRegisteredAfterSql = @"
            UPDATE [dbo].[UserPasskeys] SET
                [RevokedAt] = SYSUTCDATETIME(),
                [RevokedReason] = @Reason,
                [RevokedBy] = @RevokedBy
            OUTPUT " + InsertedColumns + @"
            WHERE [UserId] = @UserId
              AND [RevokedAt] IS NULL
              AND [CreatedAt] > @RegisteredAfter";

    // A sign-in finds its passkey by the credential id the response names (S20 PR B, PK8): bytes
    // compared as bytes (VARBINARY) through UX_UserPasskeys_CredentialId, live, of the RP ID in
    // force. Any account's: the user handle the handler compares next says whose it must be.
    internal const string LiveByCredentialIdSql = @"
            SELECT" + Columns + @"
            FROM [dbo].[UserPasskeys]
            WHERE [CredentialId] = @CredentialId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL";

    // The owner's handle, and nothing else of the owner, whatever the account's state: a
    // soft-deleted account included (FA6).
    internal const string UserHandleSql = @"
            SELECT [WebAuthnUserHandle] FROM [dbo].[Users] WHERE [Id] = @UserId";

    // A verified sign-in, recorded in one statement (PK9, T-B4): only while the passkey is still
    // live and its counter is the one the sign-in was checked against. A removal, a reset or
    // another sign-in that got there first matches nothing, and the sign-in is refused.
    internal const string RecordAssertionSql = @"
            UPDATE [dbo].[UserPasskeys] SET
                [SignCount] = @SignCount,
                [LastUsedAt] = @LastUsedAt,
                [IsBackupEligible] = @IsBackupEligible,
                [IsBackedUp] = @IsBackedUp
            WHERE [Id] = @Id
              AND [RevokedAt] IS NULL
              AND [SignCount] = @StoredSignCount";

    // A counter that did not grow (PK9): the seal, and nothing else. The passkey is never
    // removed or revoked by it; that is its owner's choice, or an administrator's.
    internal const string SealCounterRegressionSql = @"
            UPDATE [dbo].[UserPasskeys] SET [LastCounterRegressionAt] = @At
            WHERE [Id] = @Id";

    internal const string RenameSql = @"
            UPDATE [dbo].[UserPasskeys] SET [Name] = @Name
            WHERE [Id] = @Id
              AND [UserId] = @UserId
              AND [RpId] = @RpId
              AND [RevokedAt] IS NULL";

    // SQL Server's unique-index and unique-constraint violations.
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasskeySchema _schema;

    public UserPasskeyRepository(IDbConnectionFactory connectionFactory, IPasskeySchema schema)
    {
        _connectionFactory = connectionFactory;
        _schema = schema;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserPasskey>> GetLiveAsync(Guid userId, string rpId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return [];
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserPasskeyDto>(new CommandDefinition(
            LiveSql,
            new { UserId = userId, RpId = rpId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<UserPasskey?> GetLiveByIdAsync(Guid userId, Guid passkeyId, string rpId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserPasskeyDto>(new CommandDefinition(
            LiveByIdSql,
            new { Id = passkeyId, UserId = userId, RpId = rpId },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<int> CountLiveAsync(Guid userId, string rpId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return 0;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            CountLiveSql,
            new { UserId = userId, RpId = rpId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<UserPasskey?> GetLiveByCredentialIdAsync(byte[] credentialId, string rpId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserPasskeyDto>(new CommandDefinition(
            LiveByCredentialIdSql,
            // A byte[] is sent as VARBINARY: the comparison is of bytes, through the unique
            // index, never of text under a case-insensitive collation.
            new { CredentialId = credentialId, RpId = rpId },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetUserHandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
            UserHandleSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> TryRecordAssertionAsync(UserPasskey passkey, long storedSignCount, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return false;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            RecordAssertionSql,
            new
            {
                passkey.Id,
                passkey.SignCount,
                passkey.LastUsedAt,
                passkey.IsBackupEligible,
                passkey.IsBackedUp,
                StoredSignCount = storedSignCount,
            },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <inheritdoc />
    public async Task SealCounterRegressionAsync(Guid passkeyId, DateTime atUtc, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SealCounterRegressionSql,
            new { Id = passkeyId, At = atUtc },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> HasLiveOfAnotherRpIdAsync(string rpId, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return false;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(@"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserPasskeys]
                WHERE [RevokedAt] IS NULL
                  AND [RpId] <> @RpId)
            THEN 1 ELSE 0 END AS BIT)",
            new { RpId = rpId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<byte[]?> EnsureUserHandleAsync(Guid userId, byte[] candidate, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
            EnsureUserHandleSql,
            new { UserId = userId, Candidate = candidate },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<PasskeyInsertOutcome> TryCreateAsync(
        UserPasskey passkey,
        int maxLive,
        Guid? bindCodeId,
        Guid sessionId,
        bool requireAnotherFactor,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            throw new InvalidOperationException("The passkey schema is not deployed: no passkey can be stored.");
        }

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            // The emailed code first, when the bind needed one (X02): spent only by a bind
            // that commits, and refused when another request spent or superseded it.
            if (bindCodeId is { } codeId
                && await connection.ExecuteAsync(new CommandDefinition(
                    SingleUseCodeStatements.TwoFactorBindCodes.Consume,
                    new { Id = codeId },
                    transaction,
                    cancellationToken: cancellationToken)) != 1)
            {
                transaction.Rollback();
                return PasskeyInsertOutcome.BindCodeLost;
            }

            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                InsertWithinLimitSql,
                new
                {
                    passkey.Id,
                    passkey.UserId,
                    passkey.CredentialId,
                    passkey.PublicKey,
                    passkey.SignCount,
                    passkey.RpId,
                    passkey.IsUserVerified,
                    passkey.IsBackupEligible,
                    passkey.IsBackedUp,
                    passkey.Name,
                    passkey.CreatedAt,
                    MaxLive = maxLive,
                    SessionId = sessionId,
                    RequireAnotherFactor = requireAnotherFactor,
                },
                transaction,
                cancellationToken: cancellationToken));

            if (inserted != 1)
            {
                var sessionLive = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    SessionLiveSql,
                    new { SessionId = sessionId, passkey.UserId },
                    transaction,
                    cancellationToken: cancellationToken));
                var anotherFactor = !requireAnotherFactor || !sessionLive
                    || await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        AnotherFactorSql,
                        new { passkey.UserId, passkey.RpId },
                        transaction,
                        cancellationToken: cancellationToken));
                transaction.Rollback();
                return !sessionLive ? PasskeyInsertOutcome.SessionEnded
                    : !anotherFactor ? PasskeyInsertOutcome.AnotherFactorRequired
                    : PasskeyInsertOutcome.LimitReached;
            }

            transaction.Commit();
            return PasskeyInsertOutcome.Inserted;
        }
        catch (SqlException ex) when (DuplicateKeyErrors.Contains(ex.Number))
        {
            transaction.Rollback();
            return PasskeyInsertOutcome.DuplicateCredential;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRenameAsync(Guid userId, Guid passkeyId, string rpId, string name, CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return false;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            RenameSql,
            new { Id = passkeyId, UserId = userId, RpId = rpId, Name = name },
            cancellationToken: cancellationToken)) == 1;
    }

    /// <inheritdoc />
    public async Task<UserPasskey?> TryRevokeAsync(
        Guid userId,
        Guid passkeyId,
        string rpId,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserPasskeyDto>(new CommandDefinition(
            RevokeOneSql,
            new { Id = passkeyId, UserId = userId, RpId = rpId, Reason = reason, RevokedBy = revokedBy },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<UserPasskey?> TryRevokeUnlessLastAsync(
        Guid userId,
        Guid passkeyId,
        string rpId,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return null;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserPasskeyDto>(new CommandDefinition(
            RevokeOneUnlessLastSql,
            new { Id = passkeyId, UserId = userId, RpId = rpId, Reason = reason, RevokedBy = revokedBy },
            cancellationToken: cancellationToken));

        return row?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserPasskey>> RevokeRegisteredAfterAsync(
        Guid userId,
        DateTime registeredAfterUtc,
        string reason,
        Guid? revokedBy,
        CancellationToken cancellationToken)
    {
        if (!await _schema.IsPresentAsync(cancellationToken))
        {
            return [];
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserPasskeyDto>(new CommandDefinition(
            RevokeRegisteredAfterSql,
            new { UserId = userId, RegisteredAfter = registeredAfterUtc, Reason = reason, RevokedBy = revokedBy },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToEntity()).ToList();
    }

    /// <summary>A passkey row as Dapper reads it.</summary>
    internal sealed record UserPasskeyDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public byte[] CredentialId { get; init; } = [];
        public byte[] PublicKey { get; init; } = [];
        public long SignCount { get; init; }
        public string RpId { get; init; } = string.Empty;
        public bool IsUserVerified { get; init; }
        public bool IsBackupEligible { get; init; }
        public bool IsBackedUp { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime? LastUsedAt { get; init; }
        public DateTime? LastCounterRegressionAt { get; init; }
        public DateTime? RevokedAt { get; init; }
        public string? RevokedReason { get; init; }
        public Guid? RevokedBy { get; init; }

        public UserPasskey ToEntity() => new(
            Id, UserId, CredentialId, PublicKey, SignCount, RpId, IsUserVerified, IsBackupEligible, IsBackedUp,
            Name, CreatedAt, LastUsedAt, LastCounterRegressionAt, RevokedAt, RevokedReason, RevokedBy);
    }
}
