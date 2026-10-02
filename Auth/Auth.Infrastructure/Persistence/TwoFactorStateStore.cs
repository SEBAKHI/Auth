using System.Collections.Frozen;
using System.Data;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;
using Microsoft.Data.SqlClient;

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
    // The column is written only by statements of this store: raised by this
    // claim and by switching the factor on, both through the CASE below, and
    // cleared when a pending secret is replaced — a new secret has no accepted code.
    private const string StepIsNewer =
        "(@RejectReused = 0 OR [LastUsedTimeStep] IS NULL OR [LastUsedTimeStep] < @Step)";

    private const string RaiseStep =
        "[LastUsedTimeStep] = CASE WHEN [LastUsedTimeStep] >= @Step THEN [LastUsedTimeStep] ELSE @Step END";

    private const string TotpStepClaimSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                " + RaiseStep + @"
            OUTPUT deleted.[LastUsedTimeStep]
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND " + StepIsNewer;

    // Switching the factor on (contract A3d). The pending row is enabled only while
    // it still holds the secret the code was checked against — the ciphertext as
    // read, because the encryption uses a random nonce and re-encrypting the same
    // secret never compares equal — so a code checked against a secret that a
    // concurrent setup has since replaced enables nothing. IsEnabled = 0 makes the
    // first of two concurrent enables the only one: the second matches no row and
    // never shows its recovery codes. The code's step is claimed in the same
    // statement, so the code that switched the factor on cannot sign in again.
    private const string EnableSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [IsEnabled] = 1,
                [EnabledAt] = SYSUTCDATETIME(),
                [RecoveryCodes] = @RecoveryCodes,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                " + RaiseStep + @"
            OUTPUT deleted.[LastUsedTimeStep]
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 0
              AND [SecretKey] = @SecretSeen
              AND " + StepIsNewer;

    // Switching the factor off with an authenticator-app code: the row goes only
    // while the code's step is newer than the last one accepted — the condition of
    // the step claim — so the code a user just signed in with cannot switch the
    // factor off a moment later, and of two requests carrying one code only one
    // matches. OUTPUT tells a reuse let through by the switch from a first use.
    private const string RemoveWithTotpSql = @"
            DELETE FROM [dbo].[TwoFactorAuth]
            OUTPUT deleted.[LastUsedTimeStep]
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND " + StepIsNewer;

    // Switching the factor off with a recovery code: the row goes only while it
    // still holds the exact set the code was checked against (contract A3c's
    // condition), so a code spent by a concurrent sign-in switches nothing off.
    private const string RemoveWithRecoveryCodeSql = @"
            DELETE FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND [RecoveryCodes] = @OldCodes";

    // The account flag the sign-in gate reads, written only here and only inside
    // the transactions that switch the factor on or off. No condition on its own
    // value: switching off also repairs a flag that said on while no factor was.
    private const string SetAccountFlagSql = @"
            UPDATE [dbo].[Users] SET
                [IsTwoFactorEnabled] = @IsTwoFactorEnabled,
                [ModifiedAt] = SYSUTCDATETIME(),
                [ModifiedBy] = @UserId
            WHERE [Id] = @UserId";

    // Replacing the secret of a pending factor in place (contract A3e). Only a row
    // that is not enabled matches, so setup can never replace the secret of a
    // factor in use; the failure count and the lock stay as they are, so starting
    // setup again does not clear a lock that guessing at enable earned.
    private const string RotatePendingSecretSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [SecretKey] = @SecretKey,
                [LastUsedTimeStep] = NULL,
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 0";

    private const string InsertPendingSql = @"
            INSERT INTO [dbo].[TwoFactorAuth] ([Id], [UserId], [SecretKey], [IsEnabled], [FailedAttempts], [CreatedAt])
            VALUES (@Id, @UserId, @SecretKey, 0, 0, SYSUTCDATETIME())";

    private const string ReadIsEnabledSql = @"
            SELECT [IsEnabled]
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId";

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

    // How a proof settles the factor inside a transaction — the sign-in commit, or
    // switching the factor off — chosen by the proof's method rather than by
    // branching on it.
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

    // How a proof removes the factor when it is switched off, chosen the same way.
    // A refusal answers with the outcome it means while the factor is still on;
    // a read after the rollback decides whether the factor is on at all.
    private static readonly FrozenDictionary<SecondFactorMethod, SettleAsync> Removers =
        new Dictionary<SecondFactorMethod, SettleAsync>
        {
            [SecondFactorMethod.Totp] = RemoveWithTotpAsync,
            [SecondFactorMethod.RecoveryCode] = RemoveWithRecoveryCodeAsync,
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
            outcome = await ClassifyRefusalAsync(connection, userId, outcome, cancellationToken);
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
            ? await ClassifyRefusalAsync(connection, userId, outcome, cancellationToken)
            : outcome;
    }

    /// <inheritdoc />
    public async Task<bool> TryStorePendingSecretAsync(
        Guid userId,
        string protectedSecretKey,
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        if (await RotatePendingSecretAsync(connection, userId, protectedSecretKey, cancellationToken))
        {
            return true;
        }

        var isEnabled = await ReadIsEnabledAsync(connection, userId, cancellationToken);
        if (isEnabled is not null)
        {
            // A row the rotation did not match: one that is enabled — or a pending
            // one a concurrent setup inserted a moment ago, which one more rotation
            // takes over.
            return isEnabled == false
                && await RotatePendingSecretAsync(connection, userId, protectedSecretKey, cancellationToken);
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertPendingSql,
                new { Id = Guid.NewGuid(), UserId = userId, SecretKey = protectedSecretKey },
                cancellationToken: cancellationToken));

            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent setup inserted the user's row first (UQ_TwoFactorAuth_UserId).
            // Rotate it once; if it was enabled in the meantime, nothing matches.
            return await RotatePendingSecretAsync(connection, userId, protectedSecretKey, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryEnableAsync(
        Guid userId,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await EnableAsync(
            connection, userId, protectedSecretSeen, recoveryCodesJson, step, rejectReusedSteps, cancellationToken);

        if (outcome is LoginCommitOutcome.Committed or LoginCommitOutcome.ReuseAccepted)
        {
            return outcome;
        }

        // Named after the rollback, holding nothing: the factor is on, so another
        // request won; or the pending row is gone, or holds another secret now.
        return await ClassifyRefusalAsync(connection, userId, LoginCommitOutcome.AlreadyEnabled, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryDisableAsync(
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        var remove = Removers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await DisableAsync(connection, userId, proof, remove, rejectReusedSteps, cancellationToken);

        if (outcome is LoginCommitOutcome.Committed or LoginCommitOutcome.ReuseAccepted)
        {
            return outcome;
        }

        // Named after the rollback, holding nothing: with no enabled factor left
        // the factor is lost; with one, the proof itself was refused.
        return await ClassifyRefusalAsync(connection, userId, outcome, cancellationToken);
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
    /// Switches the factor on: the pending row, then the account flag, in one
    /// transaction committed only when each matched exactly one row.
    /// </summary>
    private static async Task<LoginCommitOutcome> EnableAsync(
        IDbConnection connection,
        Guid userId,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        // The factor row first, then the account row — the order every lifecycle
        // transaction takes, so two of them can never deadlock each other.
        var enabled = await connection.QuerySingleOrDefaultAsync<StepClaimDto>(new CommandDefinition(
            EnableSql,
            new
            {
                UserId = userId,
                SecretSeen = protectedSecretSeen,
                RecoveryCodes = recoveryCodesJson,
                Step = step,
                RejectReused = rejectReusedSteps
            },
            transaction,
            cancellationToken: cancellationToken));

        if (enabled is null || !await SetAccountFlagAsync(connection, transaction, userId, true, cancellationToken))
        {
            // Refused; the caller names why, once nothing is held.
            transaction.Rollback();
            return LoginCommitOutcome.FactorLost;
        }

        transaction.Commit();
        return enabled.LastUsedTimeStep >= step
            ? LoginCommitOutcome.ReuseAccepted
            : LoginCommitOutcome.Committed;
    }

    /// <summary>
    /// Switches the factor off: the enabled row, then the account flag, in one
    /// transaction committed only when each matched exactly one row.
    /// </summary>
    private static async Task<LoginCommitOutcome> DisableAsync(
        IDbConnection connection,
        Guid userId,
        SecondFactorProof proof,
        SettleAsync remove,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        var removed = await remove(connection, transaction, userId, proof, rejectReusedSteps, cancellationToken);

        if (removed is not (LoginCommitOutcome.Committed or LoginCommitOutcome.ReuseAccepted))
        {
            transaction.Rollback();
            return removed;
        }

        if (!await SetAccountFlagAsync(connection, transaction, userId, false, cancellationToken))
        {
            // No account row to clear: nothing is left to switch off.
            transaction.Rollback();
            return LoginCommitOutcome.FactorLost;
        }

        transaction.Commit();
        return removed;
    }

    private static async Task<LoginCommitOutcome> RemoveWithTotpAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        var step = proof.Step ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched.");

        var removed = await connection.QuerySingleOrDefaultAsync<StepClaimDto>(new CommandDefinition(
            RemoveWithTotpSql,
            new { UserId = userId, Step = step, RejectReused = rejectReusedSteps },
            transaction,
            cancellationToken: cancellationToken));

        if (removed is null)
        {
            return LoginCommitOutcome.StepReused;
        }

        return removed.LastUsedTimeStep >= step
            ? LoginCommitOutcome.ReuseAccepted
            : LoginCommitOutcome.Committed;
    }

    private static async Task<LoginCommitOutcome> RemoveWithRecoveryCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken)
    {
        // A recovery code has no time step; the reuse rule does not apply to it.
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            RemoveWithRecoveryCodeSql,
            new { UserId = userId, OldCodes = proof.OldCodesJson },
            transaction,
            cancellationToken: cancellationToken));

        return removed == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.RecoveryCodesChanged;
    }

    private static async Task<bool> SetAccountFlagAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        bool isTwoFactorEnabled,
        CancellationToken cancellationToken)
    {
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            SetAccountFlagSql,
            new { UserId = userId, IsTwoFactorEnabled = isTwoFactorEnabled },
            transaction,
            cancellationToken: cancellationToken));

        return updated == 1;
    }

    private static async Task<bool> RotatePendingSecretAsync(
        IDbConnection connection,
        Guid userId,
        string protectedSecretKey,
        CancellationToken cancellationToken)
    {
        // One statement, so no transaction: its affected row is the decision.
        var rotated = await connection.ExecuteAsync(new CommandDefinition(
            RotatePendingSecretSql,
            new { UserId = userId, SecretKey = protectedSecretKey },
            cancellationToken: cancellationToken));

        return rotated == 1;
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
    /// Names why a write matched no row: the factor is gone or switched off, or it
    /// is on — and then the refusal means <paramref name="refusedWhileEnabled"/>:
    /// a step that was not newer, a recovery-code set that changed, or a factor
    /// another request switched on first. Read once nothing is held — after the
    /// transaction rolled back — and never written from.
    /// </summary>
    private static async Task<LoginCommitOutcome> ClassifyRefusalAsync(
        IDbConnection connection,
        Guid userId,
        LoginCommitOutcome refusedWhileEnabled,
        CancellationToken cancellationToken)
    {
        var isEnabled = await ReadIsEnabledAsync(connection, userId, cancellationToken);

        return isEnabled == true ? refusedWhileEnabled : LoginCommitOutcome.FactorLost;
    }

    /// <returns>Whether the user's factor is enabled, or null when there is no row.</returns>
    private static Task<bool?> ReadIsEnabledAsync(
        IDbConnection connection,
        Guid userId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            ReadIsEnabledSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));

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
