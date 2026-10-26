using System.Collections.Frozen;
using System.Data;
using System.Globalization;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

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
    // There is no setting that lets a reused step through: RaiseStep is only ever
    // written next to StepIsNewer, so for the same secret the step it writes is
    // always newer than the one it replaces.
    // The column is written only by statements of this store: raised through
    // RaiseStep by this claim, by switching the factor on (EnableSql) and by
    // switching the app on in place of the emailed code (EnableOverEmailCodeSql);
    // set unconditionally by ConfirmReplacementSql to the step of the NEW secret's
    // code, since no code of that secret was accepted before; and cleared by
    // RotatePendingSecretSql and BindEmailCodeOverRowSql, whose new secret, or no
    // secret, has no accepted code.
    private const string StepIsNewer =
        "([LastUsedTimeStep] IS NULL OR [LastUsedTimeStep] < @Step)";

    // Only ever written next to StepIsNewer, which makes @Step the larger value.
    private const string RaiseStep =
        "[LastUsedTimeStep] = @Step";

    private const string TotpStepClaimSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                " + RaiseStep + @"
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND " + StepIsNewer;

    // One failed attempt counted against the factor before any code is checked
    // (contract A2). The count and the timed lock move in one statement, and a
    // locked row matches nothing. However many requests arrive together, each one
    // that is let through has already been counted, and the one that reaches the
    // maximum locks the factor for all the others — so at most MaxFailedAttempts
    // codes are ever checked before the lock, never one per concurrent request.
    // Consequence, fail-closed: a correct code presented at the exact instant the
    // fifth failure locks the factor is refused LockedOut before it is checked; the
    // winning sign-in clears the lock, so at rest it is not locked. It denies,
    // never grants.
    // The hard limit (X12, ARR-0502): a count at @Hard matches nothing either, for
    // good — every caller, a factor still pending included, but one: the recovery of
    // a deletion the account's own signed-in session asked for reserves with @Hard
    // NULL (owner decisions 2026-10-12, X12 PR B row «pending deletion» (b), narrowed
    // by D-66-1 (a)). Only a successful code settles the count, and only a
    // reservation precedes one, so a count at the limit stays there until the factor
    // is reset, or a correct code is presented on that one path: no password reset
    // and no waiting lowers it. One statement, so of a burst at @Hard - 1
    // exactly one request is let through, and its OUTPUT count, equal to @Hard, is
    // the one transition. @Hard NULL (the Legacy mode) caps nothing.
    internal const string ReserveAttemptSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [FailedAttempts] = [FailedAttempts] + 1,
                [LockedUntil] = CASE WHEN [FailedAttempts] + 1 >= @MaxAttempts
                                     THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
                                     ELSE [LockedUntil] END,
                [ModifiedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[FailedAttempts], inserted.[LockedUntil]
            WHERE [UserId] = @UserId
              AND ([LockedUntil] IS NULL OR [LockedUntil] <= SYSUTCDATETIME())
              AND (@Hard IS NULL OR [FailedAttempts] < @Hard)";

    // The emailed code's own reservation (S24, FA4 (4-3)): A2's statement on the
    // emailed code's columns alone, so wrong emailed codes never lock the
    // authenticator app or the recovery codes, nor the other way round (NIST SP
    // 800-63B §3.2.2 counts per authenticator). The same five-and-fifteen timed lock
    // and the same consecutive-failure limit; a row whose factor is not the emailed
    // code matches nothing. ModifiedAt is stamped as by every counted failure, so the
    // gate in front of an administrator's reset (LK4 (4)) reads the time of the
    // failure that reached the limit. A resend lowers nothing: only a correct code,
    // or a completed password reset, settles this count.
    internal const string ReserveEmailCodeAttemptSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [EmailFailedAttempts] = [EmailFailedAttempts] + 1,
                [EmailLockedUntil] = CASE WHEN [EmailFailedAttempts] + 1 >= @MaxAttempts
                                          THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
                                          ELSE [EmailLockedUntil] END,
                [ModifiedAt] = SYSUTCDATETIME()
            OUTPUT inserted.[EmailFailedAttempts]
            WHERE [UserId] = @UserId
              AND [EmailCodeEnabledAt] IS NOT NULL
              AND ([EmailLockedUntil] IS NULL OR [EmailLockedUntil] <= SYSUTCDATETIME())
              AND (@Hard IS NULL OR [EmailFailedAttempts] < @Hard)";

    // A correct emailed code settles its own count and lock, and nothing else: the
    // authenticator app's count is not the emailed code's to clear.
    private const string SettleEmailCodeSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [EmailFailedAttempts] = 0,
                [EmailLockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [EmailCodeEnabledAt] IS NOT NULL";

    // The emailed code is spent with its challenge, and only while the challenge
    // still holds the codes it was checked against: a code a newer send replaced in
    // the moment between the check and this commit signs nobody in. The hashes are
    // cleared, so a spent challenge keeps no code.
    private const string SpendEmailCodesSql = @"
            UPDATE [dbo].[TwoFactorChallenges] SET
                [EmailCodeHash] = NULL
            WHERE [Id] = @ChallengeId
              AND [UserId] = @UserId
              AND [EmailCodeHash] = @CodesSeen";

    // A completed password reset clears the emailed code's count and lock (LK4 (c)):
    // the emailed code is the only factor whose way out of the hard lock is the reset,
    // because the reset proves the same mailbox. The authenticator app's count is
    // never touched by a reset (ARR-0502). ModifiedAt is left as it is: the
    // administrator's reset gate reads it as the last counted failure and opens only
    // for a password changed after it, and this clearing is no failure — stamping it
    // would close the gate on the very reset that should open it.
    private const string ClearEmailCodeLockSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [EmailFailedAttempts] = 0,
                [EmailLockedUntil] = NULL
            WHERE [UserId] = @UserId
              AND ([EmailFailedAttempts] > 0 OR [EmailLockedUntil] IS NOT NULL)";

    // Switching the factor on (contract A3d). The pending row is enabled only while
    // it still holds the secret the code was checked against — the ciphertext as
    // read, because the encryption uses a random nonce and re-encrypting the same
    // secret never compares equal — so a code checked against a secret that a
    // concurrent setup has since replaced enables nothing. IsEnabled = 0 makes the
    // first of two concurrent enables the only one: the second matches no row and
    // never shows its recovery codes. The code's step is claimed in the same
    // statement, so the code that switched the factor on cannot sign in again.
    // Codes issued with the authenticator app count as a second factor (FA3), named
    // here because the column's default is 0. A row whose factor is the emailed code
    // is not switched over here: replacing it needs a proof of its own and clears the
    // emailed code in the same statement (EnableOverEmailCodeSql), and
    // CK_TwoFactorAuth_SingleFactor would refuse this write anyway.
    private const string EnableSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [IsEnabled] = 1,
                [EnabledAt] = SYSUTCDATETIME(),
                [RecoveryCodes] = @RecoveryCodes,
                [RecoveryCodesQualified] = 1,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                " + RaiseStep + @"
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 0
              AND [EmailCodeEnabledAt] IS NULL
              AND [SecretKey] = @SecretSeen
              AND " + StepIsNewer;

    // Switching the factor off with an authenticator-app code: the row goes only
    // while the code's step is newer than the last one accepted — the condition of
    // the step claim — so the code a user just signed in with cannot switch the
    // factor off a moment later, and of two requests carrying one code only one
    // matches.
    private const string RemoveWithTotpSql = @"
            DELETE FROM [dbo].[TwoFactorAuth]
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

    // The one definition of "the account has a second factor" the platform-
    // administrator policy reads: an enabled two-factor row. Never the account
    // flag, which can disagree with the row. Both statements below are built from
    // it, so a later factor (a passkey) is added here once.
    // Unqualified columns resolve to the subquery's own table first.
    private const string EnabledFactorOf = @"EXISTS (
                SELECT 1 FROM [dbo].[TwoFactorAuth]
                WHERE [UserId] = {0} AND [IsEnabled] = 1)";

    private static readonly string HasEnabledFactorSql = @"
            SELECT CAST(CASE WHEN " + string.Format(CultureInfo.InvariantCulture, EnabledFactorOf, "@UserId") + @"
                THEN 1 ELSE 0 END AS BIT)";

    // A platform holder of the role — the predicates the platform token reads
    // (PermissionRepository's platform query; the pre-switch inventory) — whose
    // account has no enabled factor. Accounts waiting for deletion count too: one
    // its owner recovers comes back with its roles (RecoverAccountCommandHandler),
    // and the deployment guide's inventory lists them for the same reason.
    private static readonly string HasPlatformRoleHolderWithoutFactorSql = @"
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM [dbo].[UserRoles] ur
                INNER JOIN [dbo].[Roles] r ON r.[Id] = ur.[RoleId]
                WHERE ur.[RoleId] = @RoleId
                  AND ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL
                  AND ur.[IsActive] = 1 AND r.[IsActive] = 1
                  AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
                  AND NOT " + string.Format(CultureInfo.InvariantCulture, EnabledFactorOf, "ur.[UserId]") + @")
                THEN 1 ELSE 0 END AS BIT)";

    // Regenerating the recovery codes (contract A3g): the new set is written only
    // while the stored set is still the one this request saw — after its own
    // proof settled — so of two concurrent regenerations one set survives, and the
    // loser's codes are never shown. NULL matches NULL: a factor enabled without
    // codes can still be given some.
    // Renewed in a session that proved two factors, so they count as one (FA3).
    private const string ReplaceRecoveryCodesSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [RecoveryCodes] = @NewCodes,
                [RecoveryCodesQualified] = 1,
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND ([RecoveryCodes] = @OldCodes OR ([RecoveryCodes] IS NULL AND @OldCodes IS NULL))";

    // Starting a replacement: the new secret waits beside the current one, which
    // keeps working until the new one is confirmed. A second start replaces the
    // waiting secret and restarts its clock.
    private const string StorePendingReplacementSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [PendingSecretKey] = @PendingSecretKey,
                [PendingSecretCreatedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1";

    // Confirming the new authenticator (contract A3f). Only the waiting secret the
    // code was checked against — the ciphertext as read — and only while it is
    // young enough by the database's own clock, the clock that stamped it. It is
    // cleared in the same statement, so the confirmation happens once. The step
    // is the NEW secret's: no code of it was ever accepted, so the old secret's
    // claims do not carry over (as when setup rotates a pending secret). And A3g's
    // predicate: the recovery codes are replaced only while they are the set this
    // request saw, so a regeneration that committed first keeps its codes, which
    // it has shown; this confirmation is then refused and shows none.
    private const string ConfirmReplacementSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [SecretKey] = [PendingSecretKey],
                [PendingSecretKey] = NULL,
                [PendingSecretCreatedAt] = NULL,
                [RecoveryCodes] = @RecoveryCodes,
                [RecoveryCodesQualified] = 1,
                [LastUsedTimeStep] = @Step,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 1
              AND [PendingSecretKey] = @PendingSeen
              AND [PendingSecretCreatedAt] > DATEADD(MINUTE, -@LifetimeMinutes, SYSUTCDATETIME())
              AND ([RecoveryCodes] = @OldCodes OR ([RecoveryCodes] IS NULL AND @OldCodes IS NULL))";

    // An administrator's reset: the row goes whatever it holds — enabled, pending,
    // locked — and the flag is cleared, whatever it said, in one transaction.
    private const string RemoveFactorSql = @"
            DELETE FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId";

    private const string ClearAccountFlagByAdministratorSql = @"
            UPDATE [dbo].[Users] SET
                [IsTwoFactorEnabled] = 0,
                [ModifiedAt] = SYSUTCDATETIME(),
                [ModifiedBy] = @ResetBy
            WHERE [Id] = @UserId";

    // A second factor proved inside a signed-in session upgrades that session:
    // the method is OR-ed into what it already proved, never written over it.
    // ISNULL, because NULL | @Method is NULL: a session recorded before methods
    // were gets the factor alone, which proves no first factor, so it still has to
    // sign in again. Only the caller's own live row matches.
    private const string UpgradeUserSessionSql = @"
            UPDATE [dbo].[UserSessions] SET
                [AuthMethods] = ISNULL([AuthMethods], 0) | @Method
            WHERE [Id] = @SessionId
              AND [UserId] = @UserId
              AND [EndedAt] IS NULL";

    // The SSO session the request's cookie names, when it carried one: the next
    // authorization from this browser then knows the factor was proved.
    private const string UpgradeIdpSessionSql = @"
            UPDATE [dbo].[IdpSessions] SET
                [AuthMethods] = ISNULL([AuthMethods], 0) | @Method
            WHERE [TokenHash] IN @IdpHashes
              AND [UserId] = @UserId
              AND [RevokedAt] IS NULL";

    // A recovery code is spent by replacing the stored set, and only while the
    // row still holds the exact text the code was checked against. Two sign-ins
    // presenting one code on two challenges both match it, but only the first
    // finds the old set in place; the second settles nothing and issues nothing.
    // The compare is the whole set, so the rare case of two DIFFERENT valid codes
    // settled at the same instant also leaves the second with no match: it is
    // denied (never double-spent), which fails closed. Sequential use is
    // unaffected — each settle sees the set the previous one wrote.
    // A recovery code is the way out for either second factor, so a row whose
    // factor is the emailed code settles one too (S24); it clears the shared count,
    // which is the one its failures were charged to.
    private const string SettleRecoveryCodeSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [RecoveryCodes] = @NewCodes,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND ([IsEnabled] = 1 OR [EmailCodeEnabledAt] IS NOT NULL)
              AND [RecoveryCodes] = @OldCodes";

    // Switching the authenticator app on in place of the emailed code (S24, D-T34 B):
    // EnableSql's change, plus the emailed code's columns cleared in the SAME
    // statement, so the row is never both (CK_TwoFactorAuth_SingleFactor) and never
    // neither. Only a row whose factor is still the emailed code, holding the secret
    // the authenticator code was checked against; the replacement proof was spent
    // just before, in the same transaction. The new recovery codes were issued with
    // the app, so they count (FA3).
    private const string EnableOverEmailCodeSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [IsEnabled] = 1,
                [EnabledAt] = SYSUTCDATETIME(),
                [RecoveryCodes] = @RecoveryCodes,
                [RecoveryCodesQualified] = 1,
                [FailedAttempts] = 0,
                [LockedUntil] = NULL,
                [EmailCodeEnabledAt] = NULL,
                [EmailFailedAttempts] = 0,
                [EmailLockedUntil] = NULL,
                [LastUsedAt] = SYSUTCDATETIME(),
                [ModifiedAt] = SYSUTCDATETIME(),
                " + RaiseStep + @"
            WHERE [UserId] = @UserId
              AND [IsEnabled] = 0
              AND [EmailCodeEnabledAt] IS NOT NULL
              AND [SecretKey] = @SecretSeen
              AND " + StepIsNewer;

    // Binding the emailed code on an account with no factor row (S24). No secret: the
    // emailed code is the factor. Recovery codes issued here are unqualified (FA3):
    // the session that bound them may have proved the password alone. A concurrent
    // bind or setup that inserted first trips UQ_TwoFactorAuth_UserId, and this one
    // writes nothing.
    private const string InsertEmailCodeFactorSql = @"
            INSERT INTO [dbo].[TwoFactorAuth] (
                [Id], [UserId], [SecretKey], [RecoveryCodes], [IsEnabled], [FailedAttempts], [CreatedAt],
                [EmailCodeEnabledAt], [EmailFailedAttempts], [RecoveryCodesQualified])
            VALUES (
                @Id, @UserId, NULL, @RecoveryCodes, 0, 0, SYSUTCDATETIME(),
                SYSUTCDATETIME(), 0, 0)";

    // Binding the emailed code over the row the account had (S24): a pending
    // authenticator app (setup begun, never confirmed) or an enabled one (D-T34 B, the
    // emailed code in place of the app). The app's secret, its waiting secret and its
    // step go in the same statement that marks the emailed code bound, so the row is
    // never both (CK_TwoFactorAuth_SingleFactor). Only while the row is still the one
    // read: the same enabled state, secret and recovery codes; any change since —
    // another tab, a replacement — matches nothing. Recovery codes the row holds stay,
    // with their qualification (the app's, FA3); when it holds none, the new set is
    // stored unqualified. Over a pending app the shared count and its lock go with the
    // app they counted: left, they would lock the new recovery codes from the start.
    // An enabled app's count stays, as its recovery codes do.
    private const string BindEmailCodeOverRowSql = @"
            UPDATE [dbo].[TwoFactorAuth] SET
                [SecretKey] = NULL,
                [PendingSecretKey] = NULL,
                [PendingSecretCreatedAt] = NULL,
                [LastUsedTimeStep] = NULL,
                [FailedAttempts] = CASE WHEN @WasEnabled = 0 THEN 0 ELSE [FailedAttempts] END,
                [LockedUntil] = CASE WHEN @WasEnabled = 0 THEN NULL ELSE [LockedUntil] END,
                [IsEnabled] = 0,
                [EnabledAt] = NULL,
                [EmailCodeEnabledAt] = SYSUTCDATETIME(),
                [EmailFailedAttempts] = 0,
                [EmailLockedUntil] = NULL,
                [RecoveryCodes] = CASE WHEN @NewCodes IS NULL THEN [RecoveryCodes] ELSE @NewCodes END,
                [RecoveryCodesQualified] = CASE WHEN @NewCodes IS NULL THEN [RecoveryCodesQualified] ELSE 0 END,
                [ModifiedAt] = SYSUTCDATETIME()
            WHERE [UserId] = @UserId
              AND [EmailCodeEnabledAt] IS NULL
              AND [IsEnabled] = @WasEnabled
              AND ([SecretKey] = @SecretSeen OR ([SecretKey] IS NULL AND @SecretSeen IS NULL))
              AND ([RecoveryCodes] = @OldCodes OR ([RecoveryCodes] IS NULL AND @OldCodes IS NULL))";

    // Switching the emailed code off (S24): the row goes only while its factor is
    // still the emailed code; the proof was spent just before, in the same transaction.
    private const string RemoveEmailCodeFactorSql = @"
            DELETE FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId
              AND [EmailCodeEnabledAt] IS NOT NULL";

    private const string ReadHasEmailCodeSql = @"
            SELECT CAST(CASE WHEN [EmailCodeEnabledAt] IS NOT NULL THEN 1 ELSE 0 END AS BIT)
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId";

    // How a proof settles the factor inside a transaction — the sign-in commit, or
    // switching the factor off — chosen by the proof's method rather than by
    // branching on it.
    private delegate Task<LoginCommitOutcome> SettleAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken);

    private static readonly FrozenDictionary<SecondFactorMethod, SettleAsync> Settlers =
        new Dictionary<SecondFactorMethod, SettleAsync>
        {
            [SecondFactorMethod.Totp] = SettleTotpAsync,
            [SecondFactorMethod.RecoveryCode] = SettleRecoveryCodeAsync,
            [SecondFactorMethod.EmailCode] = SettleEmailCodeAsync,
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
    private readonly ILogger<TwoFactorStateStore> _logger;

    public TwoFactorStateStore(
        IDbConnectionFactory connectionFactory,
        ILogger<TwoFactorStateStore>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger ?? NullLogger<TwoFactorStateStore>.Instance;
    }

    /// <inheritdoc />
    public async Task<TwoFactorSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<TwoFactorSnapshotDto>(new CommandDefinition(@"
            SELECT [UserId], [SecretKey], [RecoveryCodes], [IsEnabled], [FailedAttempts], [LockedUntil],
                   [PendingSecretKey], [PendingSecretCreatedAt], [ModifiedAt],
                   [EmailCodeEnabledAt], [EmailFailedAttempts], [EmailLockedUntil], [RecoveryCodesQualified]
            FROM [dbo].[TwoFactorAuth]
            WHERE [UserId] = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return dto?.ToSnapshot();
    }

    /// <inheritdoc />
    public async Task<bool> HasEnabledFactorAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            HasEnabledFactorSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> HasPlatformRoleHolderWithoutFactorAsync(Guid roleId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            HasPlatformRoleHolderWithoutFactorSql,
            new { RoleId = roleId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CountEmailCodeFactorsAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
            SELECT COUNT(*)
            FROM [dbo].[TwoFactorAuth]
            WHERE [EmailCodeEnabledAt] IS NOT NULL",
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int?> TryReserveAttemptAsync(Guid userId, int? hardLimit, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var reserved = await connection.QuerySingleOrDefaultAsync<AttemptReservationDto>(new CommandDefinition(
            ReserveAttemptSql,
            new
            {
                UserId = userId,
                MaxAttempts = TwoFactorAuth.MaxFailedAttempts,
                LockoutMinutes = TwoFactorAuth.LockoutMinutes,
                Hard = hardLimit
            },
            cancellationToken: cancellationToken));

        return reserved?.FailedAttempts;
    }

    /// <inheritdoc />
    public async Task<int?> TryReserveEmailCodeAttemptAsync(Guid userId, int? hardLimit, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            ReserveEmailCodeAttemptSql,
            new
            {
                UserId = userId,
                MaxAttempts = TwoFactorAuth.MaxFailedAttempts,
                LockoutMinutes = TwoFactorAuth.LockoutMinutes,
                Hard = hardLimit
            },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TrySpendRecoveryCodeAsync(
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        if (proof.Method != SecondFactorMethod.RecoveryCode)
        {
            throw new ArgumentException("Only a recovery-code proof is spent on its own.", nameof(proof));
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so no transaction: its affected row is the decision.
        var spent = await connection.ExecuteAsync(new CommandDefinition(
            SettleRecoveryCodeSql,
            new { UserId = userId, OldCodes = proof.OldCodesJson, NewCodes = proof.NewCodesJson },
            cancellationToken: cancellationToken));

        return spent == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.FactorLost;
    }

    /// <inheritdoc />
    public async Task ClearEmailCodeLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so no transaction; a row with nothing to clear matches nothing.
        await connection.ExecuteAsync(new CommandDefinition(
            ClearEmailCodeLockSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public Task<LoginCommitOutcome> TryCommitLoginAsync(
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken) =>
        TryCommitChallengeAsync(
            challengeId, TwoFactorChallengePurpose.SignIn, userId, proof, cancellationToken);

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryCommitChallengeAsync(
        Guid challengeId,
        TwoFactorChallengePurpose purpose,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        var settle = Settlers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await CommitAsync(
            connection, challengeId, purpose, userId, proof, settle, cancellationToken);

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
                  AND [Purpose] = @Purpose
                  AND [UsedAt] IS NOT NULL
                  AND [AttemptCount] > 0",
                new { ChallengeId = challengeId, Purpose = purpose },
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
        CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so no transaction: its affected row is the decision.
        var outcome = await ClaimStepAsync(
            connection, transaction: null, userId, step, cancellationToken);

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
        Guid? bindCodeId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var (outcome, upgraded) = await EnableAsync(
            connection, userId, protectedSecretSeen, recoveryCodesJson, step, bindCodeId,
            sessionUpgrade, cancellationToken);

        if (outcome == LoginCommitOutcome.Committed)
        {
            ReportUpgrade(userId, sessionUpgrade, upgraded, "switching two-factor on");
            return outcome;
        }

        // The emailed code was spent by a concurrent request first, and nothing was
        // written. The factor's state is not the reason, so it is not read: naming
        // it would tell the caller "set up again" or "already on" for a code problem.
        if (outcome == LoginCommitOutcome.ChallengeLost)
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
        CancellationToken cancellationToken)
    {
        var remove = Removers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await DisableAsync(connection, userId, proof, remove, cancellationToken);

        if (outcome == LoginCommitOutcome.Committed)
        {
            return outcome;
        }

        // Named after the rollback, holding nothing: with no enabled factor left
        // the factor is lost; with one, the proof itself was refused.
        return await ClassifyRefusalAsync(connection, userId, outcome, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryCommitStepUpAsync(
        Guid userId,
        SecondFactorProof proof,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        var settle = Settlers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var (outcome, upgraded) = await StepUpAsync(
            connection, userId, proof, settle, sessionUpgrade, cancellationToken);

        if (outcome == LoginCommitOutcome.Committed)
        {
            ReportUpgrade(userId, sessionUpgrade, upgraded, "a step-up");
            return outcome;
        }

        if (outcome == LoginCommitOutcome.SessionLost)
        {
            return outcome;
        }

        // Named after the rollback, holding nothing: with no enabled factor left
        // the factor is lost; with one, the proof itself was refused — a step not
        // newer, or a recovery-code set another sign-in changed first.
        return await ClassifyRefusalAsync(
            connection,
            userId,
            outcome == LoginCommitOutcome.StepReused
                ? LoginCommitOutcome.StepReused
                : LoginCommitOutcome.RecoveryCodesChanged,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryRegenerateCodesAsync(
        Guid userId,
        SecondFactorProof proof,
        string? codesSeen,
        string newCodesJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newCodesJson);

        // The set the new one replaces is what is stored once the proof settled:
        // for a recovery code, the set without it; otherwise the set as read.
        var oldCodes = proof.NewCodesJson ?? codesSeen;

        return await SettleThenWriteAsync(
            userId,
            proof,
            ReplaceRecoveryCodesSql,
            new { UserId = userId, OldCodes = oldCodes, NewCodes = newCodesJson },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryBeginReplacementAsync(
        Guid userId,
        SecondFactorProof proof,
        string protectedPendingSecret,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPendingSecret);

        return await SettleThenWriteAsync(
            userId,
            proof,
            StorePendingReplacementSql,
            new { UserId = userId, PendingSecretKey = protectedPendingSecret },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryConfirmReplacementAsync(
        Guid userId,
        string pendingSecretSeen,
        long step,
        string? recoveryCodesSeen,
        string recoveryCodesJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingSecretSeen);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCodesJson);

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // One statement, so no transaction: its affected row is the decision.
        var confirmed = await connection.ExecuteAsync(new CommandDefinition(
            ConfirmReplacementSql,
            new
            {
                UserId = userId,
                PendingSeen = pendingSecretSeen,
                Step = step,
                OldCodes = recoveryCodesSeen,
                RecoveryCodes = recoveryCodesJson,
                LifetimeMinutes = TwoFactorAuth.PendingReplacementLifetimeMinutes
            },
            cancellationToken: cancellationToken));

        if (confirmed == 1)
        {
            return LoginCommitOutcome.Committed;
        }

        // Named once nothing is held: with the factor still on, the waiting secret
        // was confirmed, replaced or expired first, or the codes changed meanwhile.
        return await ClassifyRefusalAsync(connection, userId, LoginCommitOutcome.ChallengeLost, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryResetAsync(Guid userId, Guid resetBy, CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Every challenge the removed factor could still answer ends with it: an
        // enrollment or a switch-off begun before the reset is not finished after it.
        // First, because every commit that spends a challenge locks the challenge
        // before the factor row; the reverse order here could deadlock with one.
        await connection.ExecuteAsync(new CommandDefinition(
            TwoFactorChallengeRepository.InvalidateEveryPurposeSql,
            new { UserId = userId },
            transaction,
            cancellationToken: cancellationToken));

        // The factor row, then the account row — the order every lifecycle
        // transaction takes. The row may be absent: an account whose flag said on
        // with no row is repaired by the same reset.
        await connection.ExecuteAsync(new CommandDefinition(
            RemoveFactorSql,
            new { UserId = userId },
            transaction,
            cancellationToken: cancellationToken));

        var cleared = await connection.ExecuteAsync(new CommandDefinition(
            ClearAccountFlagByAdministratorSql,
            new { UserId = userId, ResetBy = resetBy },
            transaction,
            cancellationToken: cancellationToken));

        if (cleared != 1)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        return true;
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryBindEmailCodeAsync(
        EmailCodeBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // The challenge first, as every commit takes it: one code binds once, and only
        // while no newer send replaced it.
        if (!await ConsumeChallengeAsync(
                connection, transaction, binding.ChallengeId, TwoFactorChallengePurpose.EmailEnrollment, cancellationToken)
            || await connection.ExecuteAsync(new CommandDefinition(
                SpendEmailCodesSql,
                new { binding.ChallengeId, binding.UserId, CodesSeen = binding.EmailCodesSeen },
                transaction,
                cancellationToken: cancellationToken)) != 1)
        {
            transaction.Rollback();
            return LoginCommitOutcome.ChallengeLost;
        }

        // Then the factor row, then the account row: the order every lifecycle
        // transaction takes.
        bool written;
        if (binding.FactorSeen is not { } seen)
        {
            try
            {
                written = await connection.ExecuteAsync(new CommandDefinition(
                    InsertEmailCodeFactorSql,
                    new { Id = Guid.NewGuid(), binding.UserId, RecoveryCodes = binding.NewRecoveryCodesJson },
                    transaction,
                    cancellationToken: cancellationToken)) == 1;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // A concurrent setup or bind inserted the account's row first
                // (UQ_TwoFactorAuth_UserId): the row this request read is not the row.
                written = false;
            }
        }
        else
        {
            written = await connection.ExecuteAsync(new CommandDefinition(
                BindEmailCodeOverRowSql,
                new
                {
                    binding.UserId,
                    WasEnabled = seen.IsEnabled,
                    SecretSeen = seen.ProtectedSecretKey,
                    OldCodes = seen.RecoveryCodes,
                    NewCodes = binding.NewRecoveryCodesJson
                },
                transaction,
                cancellationToken: cancellationToken)) == 1;
        }

        if (!written || !await SetAccountFlagAsync(connection, transaction, binding.UserId, true, cancellationToken))
        {
            transaction.Rollback();
            return LoginCommitOutcome.FactorLost;
        }

        transaction.Commit();
        return LoginCommitOutcome.Committed;
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryRemoveEmailCodeAsync(
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var outcome = await RemoveEmailCodeAsync(connection, userId, proof, cancellationToken);

        return outcome == LoginCommitOutcome.Committed
            ? outcome
            : await ClassifyEmailCodeRefusalAsync(connection, userId, outcome, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LoginCommitOutcome> TryReplaceEmailCodeWithTotpAsync(
        Guid userId,
        SecondFactorProof replacementProof,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        Guid? bindCodeId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecretSeen);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCodesJson);

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var (outcome, upgraded) = await ReplaceEmailCodeWithTotpAsync(
            connection, userId, replacementProof, protectedSecretSeen, recoveryCodesJson, step,
            bindCodeId, sessionUpgrade, cancellationToken);

        if (outcome == LoginCommitOutcome.Committed)
        {
            ReportUpgrade(userId, sessionUpgrade, upgraded, "an authenticator app replacing the emailed code");
            return outcome;
        }

        return await ClassifyEmailCodeRefusalAsync(connection, userId, outcome, cancellationToken);
    }

    /// <summary>
    /// The proof, then the row, then the account flag, in one transaction committed
    /// only when each matched exactly one row.
    /// </summary>
    private static async Task<LoginCommitOutcome> RemoveEmailCodeAsync(
        IDbConnection connection,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        var spent = await SpendReplacementProofAsync(connection, transaction, userId, proof, cancellationToken);
        if (spent != LoginCommitOutcome.Committed)
        {
            transaction.Rollback();
            return spent;
        }

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            RemoveEmailCodeFactorSql,
            new { UserId = userId },
            transaction,
            cancellationToken: cancellationToken));

        if (removed != 1 || !await SetAccountFlagAsync(connection, transaction, userId, false, cancellationToken))
        {
            transaction.Rollback();
            return LoginCommitOutcome.FactorLost;
        }

        transaction.Commit();
        return LoginCommitOutcome.Committed;
    }

    /// <summary>
    /// The bind code (when the bind needed one), the replacement proof, the row and
    /// the account flag, in one transaction committed only when each matched exactly
    /// one row; then the session upgrades, where no match is accepted (as in
    /// <see cref="EnableAsync"/>).
    /// </summary>
    private static async Task<(LoginCommitOutcome Outcome, UpgradeCounts Upgraded)> ReplaceEmailCodeWithTotpAsync(
        IDbConnection connection,
        Guid userId,
        SecondFactorProof replacementProof,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        Guid? bindCodeId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        // The bind code first, when the bind needed one (X02): spent only by a
        // replacement that commits.
        if (bindCodeId is { } codeId
            && await connection.ExecuteAsync(new CommandDefinition(
                SingleUseCodeStatements.TwoFactorBindCodes.Consume,
                new { Id = codeId },
                transaction,
                cancellationToken: cancellationToken)) != 1)
        {
            transaction.Rollback();
            return (LoginCommitOutcome.ChallengeLost, UpgradeCounts.None);
        }

        // Then the proof that the emailed code may go.
        var spent = await SpendReplacementProofAsync(connection, transaction, userId, replacementProof, cancellationToken);
        if (spent != LoginCommitOutcome.Committed)
        {
            transaction.Rollback();
            return (spent, UpgradeCounts.None);
        }

        // Then the row: the app on and the emailed code off, in one statement.
        var enabled = await connection.ExecuteAsync(new CommandDefinition(
            EnableOverEmailCodeSql,
            new
            {
                UserId = userId,
                SecretSeen = protectedSecretSeen,
                RecoveryCodes = recoveryCodesJson,
                Step = step
            },
            transaction,
            cancellationToken: cancellationToken));

        if (enabled != 1 || !await SetAccountFlagAsync(connection, transaction, userId, true, cancellationToken))
        {
            transaction.Rollback();
            return (LoginCommitOutcome.FactorLost, UpgradeCounts.None);
        }

        var upgraded = await UpgradeSessionsAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);

        transaction.Commit();
        return (LoginCommitOutcome.Committed, upgraded);
    }

    /// <summary>
    /// Spends a proof that the emailed code may be switched off or replaced, inside
    /// the caller's transaction: an emailed code consumes its switch-off challenge
    /// first and is then spent with it; a recovery code is spent by replacing the
    /// stored set. Settled by the proof's method, as every commit here is.
    /// </summary>
    private static async Task<LoginCommitOutcome> SpendReplacementProofAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        if (proof.ChallengeId is { } challengeId
            && !await ConsumeChallengeAsync(
                connection, transaction, challengeId, TwoFactorChallengePurpose.EmailDisable, cancellationToken))
        {
            return LoginCommitOutcome.ChallengeLost;
        }

        // Neither an emailed code nor a recovery code has a time step to reuse.
        var settled = await Settlers[proof.Method](
            connection, transaction, userId, proof, cancellationToken);

        // A recovery code whose set changed first was spent by another request.
        return settled == LoginCommitOutcome.FactorLost && proof.ChallengeId is null
            ? LoginCommitOutcome.RecoveryCodesChanged
            : settled;
    }

    private static async Task<bool> ConsumeChallengeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid challengeId,
        TwoFactorChallengePurpose purpose,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            SingleUseCodeStatements.TwoFactorChallenges.Consume,
            new { Id = challengeId, Purpose = purpose },
            transaction,
            cancellationToken: cancellationToken)) == 1;

    /// <summary>
    /// Names why a change to the emailed code matched no row, once nothing is held:
    /// with the emailed code no longer the account's factor, it is lost; otherwise
    /// the proof itself was refused, as <paramref name="refusal"/> says.
    /// </summary>
    private static async Task<LoginCommitOutcome> ClassifyEmailCodeRefusalAsync(
        IDbConnection connection,
        Guid userId,
        LoginCommitOutcome refusal,
        CancellationToken cancellationToken)
    {
        var hasEmailCode = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            ReadHasEmailCodeSql,
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return hasEmailCode == true ? refusal : LoginCommitOutcome.FactorLost;
    }

    /// <summary>
    /// Settles a proof and makes one further change to the enabled factor in one
    /// transaction, committed only when both matched; then names a refusal once
    /// nothing is held.
    /// </summary>
    private async Task<LoginCommitOutcome> SettleThenWriteAsync(
        Guid userId,
        SecondFactorProof proof,
        string writeSql,
        object writeParameters,
        CancellationToken cancellationToken)
    {
        var settle = Settlers[proof.Method];

        // The factory hands back an OPEN connection; opening it again throws.
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        LoginCommitOutcome outcome;
        using (var transaction = connection.BeginTransaction())
        {
            outcome = await settle(connection, transaction, userId, proof, cancellationToken);

            if (outcome == LoginCommitOutcome.Committed)
            {
                var written = await connection.ExecuteAsync(new CommandDefinition(
                    writeSql, writeParameters, transaction, cancellationToken: cancellationToken));

                if (written == 1)
                {
                    transaction.Commit();
                    return outcome;
                }

                // The proof settled, but the factor changed under it — another
                // request replaced the codes first. Nothing is kept.
                outcome = LoginCommitOutcome.RecoveryCodesChanged;
            }

            transaction.Rollback();
        }

        // Named after the rollback, holding nothing: with no enabled factor left
        // the factor is lost; with one, the proof or the write was refused.
        return await ClassifyRefusalAsync(
            connection,
            userId,
            outcome == LoginCommitOutcome.StepReused
                ? LoginCommitOutcome.StepReused
                : LoginCommitOutcome.RecoveryCodesChanged,
            cancellationToken);
    }

    /// <summary>
    /// The factor row first, as every lifecycle transaction takes it, then the
    /// session row — which must match — then the SSO session, which may not.
    /// </summary>
    private static async Task<(LoginCommitOutcome Outcome, UpgradeCounts Upgraded)> StepUpAsync(
        IDbConnection connection,
        Guid userId,
        SecondFactorProof proof,
        SettleAsync settle,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        var settled = await settle(connection, transaction, userId, proof, cancellationToken);

        if (settled != LoginCommitOutcome.Committed)
        {
            transaction.Rollback();
            return (settled, UpgradeCounts.None);
        }

        var session = await UpgradeUserSessionAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);

        if (session != 1)
        {
            // The session ended, or is not this user's, after the pre-check read
            // it. Nothing is settled: the factor stays as it was and the attempt
            // stays counted, as for any request that did not complete.
            transaction.Rollback();
            return (LoginCommitOutcome.SessionLost, UpgradeCounts.None);
        }

        var idp = await UpgradeIdpSessionAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);

        transaction.Commit();
        return (settled, new UpgradeCounts(session, idp));
    }

    /// <summary>
    /// OR-s the proven method into the caller's session row and, when the request
    /// carried the SSO cookie, into its SSO session; every statement inside the
    /// caller's transaction. Returns how many rows each matched.
    /// </summary>
    private static async Task<UpgradeCounts> UpgradeSessionsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        var session = await UpgradeUserSessionAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);
        var idp = await UpgradeIdpSessionAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);
        return new UpgradeCounts(session, idp);
    }

    private static Task<int> UpgradeUserSessionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            UpgradeUserSessionSql,
            new { SessionId = sessionUpgrade.SessionId, UserId = userId, Method = sessionUpgrade.Method.Value },
            transaction,
            cancellationToken: cancellationToken));

    /// <returns>The rows matched; 0 without a cookie, when nothing is sent.</returns>
    private static async Task<int> UpgradeIdpSessionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        if (sessionUpgrade.IdpHashes is null)
        {
            return 0;
        }

        return await connection.ExecuteAsync(new CommandDefinition(
            UpgradeIdpSessionSql,
            new { IdpHashes = sessionUpgrade.IdpHashes.ValuesFor(CredentialPurpose.IdpSession), UserId = userId, Method = sessionUpgrade.Method.Value },
            transaction,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// A committed change whose session upgrade matched no row is logged, never
    /// refused: the session row is written on a path allowed to fail, and the SSO
    /// cookie only reaches the API's own origin. Such a session keeps what it had;
    /// the next sign-in records everything.
    /// </summary>
    private void ReportUpgrade(Guid userId, SessionUpgrade sessionUpgrade, UpgradeCounts upgraded, string change)
    {
        if (upgraded.Session == 0)
        {
            _logger.LogWarning(
                "TwoFactor.SessionNotUpgraded: {Change} for user {UserId} committed, but its session {SessionId} matched no live row; that session keeps the methods it had",
                change, userId, sessionUpgrade.SessionId);
        }

        if (upgraded.Idp == 0)
        {
            _logger.LogWarning(
                "TwoFactor.IdpSessionNotUpgraded: {Change} for user {UserId} committed, but no live SSO session matched ({Reason})",
                change,
                userId,
                sessionUpgrade.IdpHashes is null ? "the request carried no SSO cookie" : "the cookie's session is gone");
        }
    }

    private static async Task<LoginCommitOutcome> CommitAsync(
        IDbConnection connection,
        Guid challengeId,
        TwoFactorChallengePurpose purpose,
        Guid userId,
        SecondFactorProof proof,
        SettleAsync settle,
        CancellationToken cancellationToken)
    {
        // An emailed code is checked against one challenge; committed against another,
        // it would spend a code that was never this one's.
        if (proof.ChallengeId is { } provenFor && provenFor != challengeId)
        {
            throw new InvalidOperationException("An emailed-code proof can only commit the challenge it was emailed for.");
        }

        using var transaction = connection.BeginTransaction();

        // The challenge row first, then the factor row: every commit takes the two
        // in the same order, so two commits can never deadlock each other.
        var consumed = await connection.ExecuteAsync(new CommandDefinition(
            SingleUseCodeStatements.TwoFactorChallenges.Consume,
            new { Id = challengeId, Purpose = purpose },
            transaction,
            cancellationToken: cancellationToken));

        if (consumed != 1)
        {
            transaction.Rollback();
            return LoginCommitOutcome.ChallengeLost;
        }

        var settled = await settle(connection, transaction, userId, proof, cancellationToken);

        if (settled != LoginCommitOutcome.Committed)
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
        CancellationToken cancellationToken) =>
        ClaimStepAsync(
            connection,
            transaction,
            userId,
            proof.Step ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched."),
            cancellationToken);

    private static async Task<LoginCommitOutcome> SettleRecoveryCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
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

    private static async Task<LoginCommitOutcome> SettleEmailCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken)
    {
        // An emailed code has no time step; its single use is the challenge's, which
        // the commit consumed first. Spent only while no newer code replaced it.
        var spent = await connection.ExecuteAsync(new CommandDefinition(
            SpendEmailCodesSql,
            new
            {
                ChallengeId = proof.ChallengeId
                    ?? throw new InvalidOperationException("An emailed-code proof must name its challenge."),
                UserId = userId,
                CodesSeen = proof.EmailCodesSeen
            },
            transaction,
            cancellationToken: cancellationToken));

        if (spent != 1)
        {
            return LoginCommitOutcome.ChallengeLost;
        }

        var settled = await connection.ExecuteAsync(new CommandDefinition(
            SettleEmailCodeSql,
            new { UserId = userId },
            transaction,
            cancellationToken: cancellationToken));

        return settled == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.FactorLost;
    }

    /// <summary>
    /// Switches the factor on: the emailed code (when the bind needs one), then the
    /// pending row, then the account flag, in one transaction committed only when
    /// each matched exactly one row; then the caller's session and SSO session are
    /// upgraded in the same transaction, where no match is accepted (contract A4's
    /// exception for the session upgrades).
    /// </summary>
    private static async Task<(LoginCommitOutcome Outcome, UpgradeCounts Upgraded)> EnableAsync(
        IDbConnection connection,
        Guid userId,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        Guid? bindCodeId,
        SessionUpgrade sessionUpgrade,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        // The emailed code first, as a sign-in consumes its challenge first: one
        // code binds at most one factor, and it is spent only together with the
        // bind — a rollback below gives it back unspent.
        if (bindCodeId is { } codeId)
        {
            var consumed = await connection.ExecuteAsync(new CommandDefinition(
                SingleUseCodeStatements.TwoFactorBindCodes.Consume,
                new { Id = codeId },
                transaction,
                cancellationToken: cancellationToken));

            if (consumed != 1)
            {
                transaction.Rollback();
                return (LoginCommitOutcome.ChallengeLost, UpgradeCounts.None);
            }
        }

        // The factor row, then the account row — the order every lifecycle
        // transaction takes, so two of them can never deadlock each other.
        var enabled = await connection.ExecuteAsync(new CommandDefinition(
            EnableSql,
            new
            {
                UserId = userId,
                SecretSeen = protectedSecretSeen,
                RecoveryCodes = recoveryCodesJson,
                Step = step
            },
            transaction,
            cancellationToken: cancellationToken));

        if (enabled != 1 || !await SetAccountFlagAsync(connection, transaction, userId, true, cancellationToken))
        {
            // Refused; the caller names why, once nothing is held.
            transaction.Rollback();
            return (LoginCommitOutcome.FactorLost, UpgradeCounts.None);
        }

        // The session the code was entered in now holds the factor too, so its next
        // refresh counts it. Matching no row does not stop the factor switching on.
        var upgraded = await UpgradeSessionsAsync(connection, transaction, userId, sessionUpgrade, cancellationToken);

        transaction.Commit();
        return (LoginCommitOutcome.Committed, upgraded);
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
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        var removed = await remove(connection, transaction, userId, proof, cancellationToken);

        if (removed != LoginCommitOutcome.Committed)
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
        CancellationToken cancellationToken)
    {
        var step = proof.Step ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched.");

        var removed = await connection.ExecuteAsync(new CommandDefinition(
            RemoveWithTotpSql,
            new { UserId = userId, Step = step },
            transaction,
            cancellationToken: cancellationToken));

        return removed == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.StepReused;
    }

    private static async Task<LoginCommitOutcome> RemoveWithRecoveryCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid userId,
        SecondFactorProof proof,
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
    /// Runs the step claim. A matched row means the factor was settled; no match
    /// means the claim refused, for a reason only a later read can name.
    /// </summary>
    private static async Task<LoginCommitOutcome> ClaimStepAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid userId,
        long step,
        CancellationToken cancellationToken)
    {
        var claimed = await connection.ExecuteAsync(new CommandDefinition(
            TotpStepClaimSql,
            new { UserId = userId, Step = step },
            transaction,
            cancellationToken: cancellationToken));

        return claimed == 1 ? LoginCommitOutcome.Committed : LoginCommitOutcome.StepReused;
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
        public string? SecretKey { get; init; }
        public string? RecoveryCodes { get; init; }
        public bool IsEnabled { get; init; }
        public int FailedAttempts { get; init; }
        public DateTime? LockedUntil { get; init; }
        public string? PendingSecretKey { get; init; }
        public DateTime? PendingSecretCreatedAt { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public DateTime? EmailCodeEnabledAt { get; init; }
        public int EmailFailedAttempts { get; init; }
        public DateTime? EmailLockedUntil { get; init; }
        public bool RecoveryCodesQualified { get; init; }

        public TwoFactorSnapshot ToSnapshot() => new(
            UserId,
            SecretKey,
            RecoveryCodes,
            IsEnabled,
            FailedAttempts,
            LockedUntil,
            PendingSecretKey,
            PendingSecretCreatedAt,
            ModifiedAt,
            EmailCodeEnabledAt,
            EmailFailedAttempts,
            EmailLockedUntil,
            RecoveryCodesQualified);
    }

    private record AttemptReservationDto
    {
        public int FailedAttempts { get; init; }
        public DateTime? LockedUntil { get; init; }
    }

    // How many rows a session upgrade matched: the session row, the SSO session.
    private readonly record struct UpgradeCounts(int Session, int Idp)
    {
        public static UpgradeCounts None => new(0, 0);
    }
}
