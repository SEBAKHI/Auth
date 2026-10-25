using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the two-factor challenge repository.
/// </summary>
/// <remarks>
/// Every statement here names the purpose it serves (S24): one table holds the
/// challenges of a sign-in, an emailed-code enrollment, a recovery and switching the
/// emailed code off, and a row of one purpose must never answer another. The sweep
/// alone covers every purpose, by expiry.
/// </remarks>
public class TwoFactorChallengeRepository : ITwoFactorChallengeRepository
{
    private const string Columns = @"
                [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt], [PrimaryMethod],
                [Purpose], [EmailCodeHash], [EmailCodeSentAt], [EmailCodeSendCount]";

    // Sending one emailed code (S24, EO6), in ONE statement whose conditions are every
    // limit on sending. The challenge: live, of the purpose, under its three sends, with
    // an attempt left (an exhausted one can never be answered, so it gets no code). The
    // factor: the account's emailed code bound and not locked — for a while or at the
    // consecutive-failure limit — unless this send binds it. The account: its codes of
    // every purpose, summed under UPDLOCK, HOLDLOCK, under the per-window and the
    // daily cap; the locks hold the account's range until the statement ends, so of a
    // burst of requests for one account exactly as many as the caps allow get through,
    // however many arrive at once. A sum counts each challenge's sends by its LAST
    // send, so a challenge whose earlier sends left the window still counts them all:
    // the caps err towards refusing.
    // The new hash is appended (CONCAT reads NULL + ';' as nothing), so the earlier
    // ones stay recognisable as superseded, and the challenge expires the lifetime
    // after this send: a resend never lowers any count. One clock, the database's.
    internal const string ReserveEmailCodeSendSql = @"
            DECLARE @Now DATETIME2 = SYSUTCDATETIME();

            UPDATE c SET
                [EmailCodeSendCount] = c.[EmailCodeSendCount] + 1,
                [EmailCodeSentAt] = @Now,
                [ExpiresAt] = DATEADD(MINUTE, @LifetimeMinutes, @Now),
                [EmailCodeHash] = CONCAT(c.[EmailCodeHash] + N';', @CodeHash)
            OUTPUT inserted.[EmailCodeSendCount] AS [SendCount], inserted.[ExpiresAt]
            FROM [dbo].[TwoFactorChallenges] c
            WHERE c.[Id] = @ChallengeId
              AND c.[Purpose] = @Purpose
              AND c.[UsedAt] IS NULL
              AND c.[ExpiresAt] > @Now
              AND c.[EmailCodeSendCount] < @PerChallengeMax
              AND c.[AttemptCount] < @MaxAttempts
              AND (@RequireBoundFactor = 0 OR EXISTS (
                    SELECT 1 FROM [dbo].[TwoFactorAuth] f
                    WHERE f.[UserId] = c.[UserId]
                      AND f.[EmailCodeEnabledAt] IS NOT NULL
                      AND ISNULL(f.[EmailLockedUntil], @Now) <= @Now
                      AND (@Hard IS NULL OR f.[EmailFailedAttempts] < @Hard)))
              AND (SELECT ISNULL(SUM(w.[EmailCodeSendCount]), 0)
                   FROM [dbo].[TwoFactorChallenges] w WITH (UPDLOCK, HOLDLOCK)
                   WHERE w.[UserId] = c.[UserId]
                     AND w.[EmailCodeSentAt] > DATEADD(SECOND, -@WindowSeconds, @Now)) < @WindowMax
              AND (SELECT ISNULL(SUM(d.[EmailCodeSendCount]), 0)
                   FROM [dbo].[TwoFactorChallenges] d WITH (UPDLOCK, HOLDLOCK)
                   WHERE d.[UserId] = c.[UserId]
                     AND d.[EmailCodeSentAt] > DATEADD(HOUR, -24, @Now)) < @DailyMax";

    // Every open challenge of the account ends, whatever its purpose (S24, OI-220 (3)):
    // a completed password reset and an administrator's two-factor reset end what the
    // challenges were opened against, so an enrollment or a switch-off begun before
    // them cannot be finished after. A new sign-in ends sign-ins alone. Shared with the
    // administrator's reset, which runs it inside its own transaction.
    internal const string InvalidateEveryPurposeSql = @"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [UsedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL";

    // The account's send budget back (S24, OI-220 (5)): the per-window and daily sums
    // count a challenge's codes by EmailCodeSentAt, so clearing it on the account's
    // ENDED challenges takes them out of both sums and changes nothing else — a live
    // challenge keeps its time and its own three-send cap. Only a completed password
    // reset runs it.
    internal const string RestoreEmailCodeSendBudgetSql = @"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [EmailCodeSentAt] = NULL
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NOT NULL
              AND [EmailCodeSentAt] IS NOT NULL";

    private readonly IDbConnectionFactory _connectionFactory;

    public TwoFactorChallengeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TwoFactorChallenge?> GetByTokenHashAsync(CredentialHashCandidates candidates, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorChallengeDto>(@"
            SELECT" + Columns + @"
            FROM [dbo].[TwoFactorChallenges]
            WHERE [TokenHash] IN @Hashes
              AND [Purpose] = @Purpose",
            new
            {
                Hashes = candidates.ValuesFor(CredentialPurpose.TwoFactorChallenge),
                Purpose = TwoFactorChallengePurpose.SignIn
            });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<TwoFactorChallenge?> GetLiveForUserAsync(
        Guid userId,
        TwoFactorChallengePurpose purpose,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorChallengeDto>(new CommandDefinition(@"
            SELECT TOP (1)" + Columns + @"
            FROM [dbo].[TwoFactorChallenges]
            WHERE [UserId] = @UserId
              AND [Purpose] = @Purpose
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > SYSUTCDATETIME()
              AND [AttemptCount] < @MaxAttempts
            ORDER BY [CreatedAt] DESC",
            new { UserId = userId, Purpose = purpose, TwoFactorChallenge.MaxAttempts },
            cancellationToken: cancellationToken));

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task CreateAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[TwoFactorChallenges] (
                [Id], [UserId], [TokenHash], [IpAddress], [ExpiresAt], [UsedAt], [AttemptCount], [CreatedAt], [PrimaryMethod],
                [Purpose]
            ) VALUES (
                @Id, @UserId, @TokenHash, @IpAddress, @ExpiresAt, @UsedAt, @AttemptCount, @CreatedAt, @PrimaryMethod,
                @Purpose
            )",
            new
            {
                challenge.Id,
                challenge.UserId,
                challenge.TokenHash,
                challenge.IpAddress,
                challenge.ExpiresAt,
                challenge.UsedAt,
                challenge.AttemptCount,
                challenge.CreatedAt,
                PrimaryMethod = challenge.PrimaryMethod.ToStored(),
                challenge.Purpose
            });
    }

    /// <inheritdoc />
    /// <remarks>
    /// The challenge is consumed by the sign-in commit, inside the transaction
    /// that also settles the factor (<see cref="TwoFactorStateStore"/>), never
    /// on its own.
    /// </remarks>
    public Task<int?> TryReserveAttemptAsync(Guid challengeId, int maxAttempts, CancellationToken cancellationToken) =>
        TryReserveAttemptAsync(challengeId, TwoFactorChallengePurpose.SignIn, maxAttempts, cancellationToken);

    /// <inheritdoc />
    public Task<int?> TryReserveAttemptAsync(
        Guid challengeId,
        TwoFactorChallengePurpose purpose,
        int maxAttempts,
        CancellationToken cancellationToken) =>
        SingleUseCodeStatements.TwoFactorChallenges.TryReserveAsync(
            _connectionFactory, challengeId, maxAttempts, new { Purpose = purpose }, cancellationToken);

    /// <inheritdoc />
    public async Task<EmailCodeSendReceipt?> TryReserveEmailCodeSendAsync(
        EmailCodeSendReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<EmailCodeSendReceipt>(new CommandDefinition(
            ReserveEmailCodeSendSql,
            new
            {
                reservation.ChallengeId,
                reservation.Purpose,
                reservation.CodeHash,
                reservation.LifetimeMinutes,
                PerChallengeMax = TwoFactorChallenge.MaxEmailCodeSends,
                TwoFactorChallenge.MaxAttempts,
                reservation.RequireBoundFactor,
                Hard = reservation.HardLimit,
                reservation.WindowSeconds,
                reservation.WindowMax,
                reservation.DailyMax
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [UsedAt] = GETUTCDATE()
            WHERE [UserId] = @UserId
              AND [UsedAt] IS NULL
              AND [Purpose] = @Purpose",
            new { UserId = userId, Purpose = TwoFactorChallengePurpose.SignIn });
    }

    /// <inheritdoc />
    public async Task InvalidateEveryPurposeForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            InvalidateEveryPurposeSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task RestoreEmailCodeSendBudgetAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            RestoreEmailCodeSendBudgetSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var deleted = await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[TwoFactorChallenges]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });

        return deleted;
    }

    // Internal DTO for mapping from database
    private record TwoFactorChallengeDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string TokenHash { get; init; } = string.Empty;
        public string? IpAddress { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime? UsedAt { get; init; }
        public int AttemptCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public int? PrimaryMethod { get; init; }
        public byte Purpose { get; init; }
        public string? EmailCodeHash { get; init; }
        public DateTime? EmailCodeSentAt { get; init; }
        public int EmailCodeSendCount { get; init; }

        public TwoFactorChallenge ToEntity() => new(
            Id,
            UserId,
            TokenHash,
            IpAddress,
            ExpiresAt,
            UsedAt,
            AttemptCount,
            CreatedAt,
            AuthenticationMethods.FromStored(PrimaryMethod),
            (TwoFactorChallengePurpose)Purpose,
            EmailCodeHash,
            EmailCodeSentAt,
            EmailCodeSendCount);
    }
}
