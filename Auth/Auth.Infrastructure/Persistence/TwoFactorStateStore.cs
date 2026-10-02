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
    // The one statement that accepts a correct authenticator-app code, in sign-in
    // and everywhere else a code is checked. It clears the failure count and the
    // lock, as every successful second factor always has, and claims the time
    // step the code matched: only while that step is newer than the last one
    // accepted, so a code counts once — however many times, and on however many
    // challenges, it is presented. Two requests carrying one code both pass the
    // check; the second finds the step taken and settles nothing.
    // IsEnabled = 1: a factor switched off after the code was checked settles
    // nothing either.
    // With @RejectReused = 0 (the rollout switch off) a reused step still settles.
    // The CASE keeps the column monotonic even then, and OUTPUT returns the step
    // the row held before, so the caller can tell that reuse from a first use.
    // The column is never written anywhere else.
    private const string TotpStepClaimSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                [LastUsedTimeStep] = CASE WHEN [LastUsedTimeStep] >= @Step THEN [LastUsedTimeStep] ELSE @Step END
            OUTPUT deleted.[LastUsedTimeStep]
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND (@RejectReused = 0 OR [LastUsedTimeStep] IS NULL OR [LastUsedTimeStep] < @Step)";

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

    // How a proof settles the factor inside the sign-in commit, chosen by the
    // proof's method rather than by branching on it.
    private delegate Task<LoginCommitOutcome> SettleAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken);

    private static readonly FrozenDictionary<SecondFactorMethod, SettleAsync> Settlers =
        new Dictionary<SecondFactorMethod, SettleAsync>
        {
            [SecondFactorMethod.Totp] = SettleTotpAsync,
            [SecondFactorMethod.RecoveryCode] = SettleRecoveryCodeAsync,
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
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        var settle = Settlers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await CommitAsync(
            connection, challengeId, userId, proof, settle, rejectReusedSteps, cancellationToken);

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
        else if (outcome == LoginCommitOutcome.StepReused)
        {
            // The step claim matched no row. Nothing is released: the challenge is
            // still live, so this attempt stays counted on it and on the account,
            // like any refused code.
            outcome = await ClassifyRefusedClaimAsync(connection, userId, cancellationToken);
        }

        return outcome;
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryClaimTotpStepAsync(
        Guid userId,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so no transaction: its affected row is the decision.
        var outcome = await ClaimStepAsync(
            connection, transaction: null, userId, step, rejectReusedSteps, cancellationToken);

        return outcome == LoginCommitOutcome.StepReused
            ? await ClassifyRefusedClaimAsync(connection, userId, cancellationToken)
            : outcome;
    }

    private static async Task<LoginCommitOutcome> CommitAsync(
        IDbConnection connection,
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        SettleAsync settle,
        bool rejectReusedSteps,
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

        var settled = await settle(connection, transaction, userId, proof, rejectReusedSteps, cancellationToken);

        if (settled is not (LoginCommitOutcome.Committed or LoginCommitOutcome.ReuseAccepted))
        {
            // The consumption rolls back with it: a proof that settled nothing
            // leaves the challenge exactly as it found it.
            transaction.Rollback();
            return settled;
        }

        transaction.Commit();
        return settled;
    }

    private static Task<LoginCommitOutcome> SettleTotpAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken) =>
        ClaimStepAsync(
            connection,
            transaction,
            userId,
            proof.Step ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched."),
            rejectReusedSteps,
            cancellationToken);

    private static async Task<LoginCommitOutcome> SettleRecoveryCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        // A recovery code has no time step; the reuse rule does not apply to it.
        var settled = await connection.ExecuteAsync(new CommandDefinition(
            SettleRecoveryCodeSql,
            new { UserId = userId, OldCodes = proof.OldCodesJson, NewCodes = proof.NewCodesJson },
            transaction,
            cancellationToken: cancellationToken));

        return settled == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.FactorLost;
    }

    /// <summary>
    /// Runs the step claim. A row back means the factor was settled; the step it
    /// held before tells a first use from a reuse let through by the switch. No
    /// row back means the claim refused, for a reason only a later read can name.
    /// </summary>
    private static async Task<LoginCommitOutcome> ClaimStepAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid userId,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        // A row DTO, not a bare long?: OUTPUT returns NULL for a first use, which a
        // scalar read could not tell apart from no row at all.
        var claimed = await connection.QuerySingleOrDefaultAsync<StepClaimDto>(new CommandDefinition(
            TotpStepClaimSql,
            new { UserId = userId, Step = step, RejectReused = rejectReusedSteps },
            transaction,
            cancellationToken: cancellationToken));

        if (claimed is null)
        {
            return LoginCommitOutcome.StepReused;
        }

        return claimed.LastUsedTimeStep >= step
            ? LoginCommitOutcome.ReuseAccepted
            : LoginCommitOutcome.Committed;
    }

    /// <summary>
    /// Names why a step claim matched no row: the factor is gone or switched off,
    /// or it is on and the step was not newer than the last one accepted. Read once
    /// nothing is held — after the commit rolled back — and never written from.
    /// </summary>
    private static async Task<LoginCommitOutcome> ClassifyRefusedClaimAsync(
        IDbConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isEnabled = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(@"
            SELECT [IsEnabled]
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return isEnabled == true ? LoginCommitOutcome.StepReused : LoginCommitOutcome.FactorLost;
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

    // The step the row held before the claim settled it (OUTPUT deleted.*).
    private record StepClaimDto
    {
        public long? LastUsedTimeStep { get; init; }
    }
}
