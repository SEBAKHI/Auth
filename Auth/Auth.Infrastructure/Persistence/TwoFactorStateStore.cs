using System.Collections.Frozen;
using System.Data;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the second-factor state store. Nothing here reads a
/// row, decides in memory and writes the row back: each change is a conditional
/// statement whose affected-row count is the decision, because two requests that
/// read the same row both pass any check made on what they read.
/// </summary>
public class TwoFactorStateStore : ITwoFactorStateStore
{
    // Settling a correct authenticator-app code clears the failure count and the
    // lock, as every successful second factor always has. IsEnabled = 1: a factor
    // switched off after the code was checked settles nothing.
    private const string SettleTotpSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1";

    // A recovery code is spent by replacing the stored set, and only while the
    // row still holds the exact text the code was checked against. Two sign-ins
    // presenting one code on two challenges both match it, but only the first
    // finds the old set in place; the second settles nothing and issues nothing.
    // The compare is the whole set, so the rare case of two DIFFERENT valid codes
    // settled at the same instant also leaves the second with no match: it is
    // denied (never double-spent), which fails closed. Sequential use is
    // unaffected — each settle sees the set the previous one wrote.
    private const string SettleRecoveryCodeSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [RecoveryCodes] = @NewCodes,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND [RecoveryCodes] = @OldCodes";

    private static readonly FrozenDictionary<SecondFactorMethod, string> SettleStatements =
        new Dictionary<SecondFactorMethod, string>
        {
            [SecondFactorMethod.Totp] = SettleTotpSql,
            [SecondFactorMethod.RecoveryCode] = SettleRecoveryCodeSql,
        }.ToFrozenDictionary();

    private readonly IDbConnectionFactory _connectionFactory;

    public TwoFactorStateStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TwoFactorSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorSnapshotDto>(new CommandDefinition(@"
            SELECT [UserId], [SecretKey], [RecoveryCodes], [IsEnabled], [FailedAttempts], [LockedUntil]
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return dto?.ToSnapshot();
    }

    /// <inheritdoc />
    public async Task<int?> TryReserveAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // The count and the lock move in one statement, and a locked row matches
        // nothing. However many requests arrive together, each one that is let
        // through has already been counted, and the one that reaches the maximum
        // locks the factor for all the others — so at most MaxFailedAttempts codes
        // are ever checked before the lock, never one per concurrent request.
        // Consequence, fail-closed: a correct code presented at the exact instant
        // the fifth failure locks the factor is refused LockedOut before it is
        // checked; the winning sign-in clears the lock, so at rest it is not
        // locked. It denies, never grants.
        var reserved = await connection.QuerySingleOrDefaultAsync<AttemptReservationDto>(new CommandDefinition(@"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = [FailedAttempts] + 1,
                [LockedUntil] = CASE WHEN [FailedAttempts] + 1 >= @MaxAttempts
                                     THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
                                     ELSE [LockedUntil] END,
                [ModifiedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[FailedAttempts], inserted.[LockedUntil]
            WHERE [UserId] = @UserId
              AND ([LockedUntil] IS NULL OR [LockedUntil] <= SYSUTCDATETIME())",
            new
            {
                UserId = userId,
                MaxAttempts = TwoFactorAuth.MaxFailedAttempts,
                LockoutMinutes = TwoFactorAuth.LockoutMinutes
            },
            cancellationToken: cancellationToken));

        return reserved?.FailedAttempts;
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryCommitLoginAsync(
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        var settle = SettleStatements[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await CommitAsync(connection, challengeId, userId, proof, settle, cancellationToken);

        if (outcome == LoginCommitOutcome.ChallengeLost)
        {
            // The code was correct, but a concurrent request consumed the challenge
            // first. Give this request's attempt back, so that at rest the count
            // stays the number of rejected codes the sign-in history shows. Only a
            // used challenge is released — a live one keeps every reservation —
            // and outside the transaction, which has already rolled back. A
            // compensation is not abandoned because the caller went away.
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE [dbo].[TwoFactorChallenges] SET
                    [AttemptCount] = [AttemptCount] - 1
                WHERE [Id] = @ChallengeId
                  AND [UsedAt] IS NOT NULL
                  AND [AttemptCount] > 0",
                new { ChallengeId = challengeId },
                cancellationToken: CancellationToken.None));
        }

        return outcome;
    }

    private static async Task<LoginCommitOutcome> CommitAsync(
        IDbConnection connection,
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        string settle,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        // The challenge row first, then the factor row: every commit takes the two
        // in the same order, so two commits can never deadlock each other.
        var consumed = await connection.ExecuteAsync(new CommandDefinition(
            SingleUseCodeStatements.TwoFactorChallenges.Consume,
            new { Id = challengeId },
            transaction,
            cancellationToken: cancellationToken));

        if (consumed != 1)
        {
            transaction.Rollback();
            return LoginCommitOutcome.ChallengeLost;
        }

        var settled = await connection.ExecuteAsync(new CommandDefinition(
            settle,
            new { UserId = userId, OldCodes = proof.OldCodesJson, NewCodes = proof.NewCodesJson },
            transaction,
            cancellationToken: cancellationToken));

        if (settled != 1)
        {
            // The consumption rolls back with it: a proof that settled nothing
            // leaves the challenge exactly as it found it.
            transaction.Rollback();
            return LoginCommitOutcome.FactorLost;
        }

        transaction.Commit();
        return LoginCommitOutcome.Committed;
    }

    // Internal DTOs for mapping from database
    private record TwoFactorSnapshotDto
    {
        public Guid UserId { get; init; }
        public string SecretKey { get; init; } = string.Empty;
        public string? RecoveryCodes { get; init; }
        public bool IsEnabled { get; init; }
        public int FailedAttempts { get; init; }
        public DateTime? LockedUntil { get; init; }

        public TwoFactorSnapshot ToSnapshot() => new(
            UserId,
            SecretKey,
            RecoveryCodes,
            IsEnabled,
            FailedAttempts,
            LockedUntil);
    }

    private record AttemptReservationDto
    {
        public int FailedAttempts { get; init; }
        public DateTime? LockedUntil { get; init; }
    }
}
