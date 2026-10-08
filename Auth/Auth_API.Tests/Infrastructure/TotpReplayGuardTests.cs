using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// A TOTP code is accepted once only if EVERY place that accepts one claims its
/// time step. One path that settles a code some other way — a whole-row write, a
/// statement of its own — is a door through which a code typed a moment ago works
/// again, and nothing else would notice. So the paths are listed, and the list is
/// held against the code: the store methods that accept a TOTP code, and the
/// Application code that checks one.
/// </summary>
/// <remarks>
/// Later items that add a way to accept a code (step-up, replacing the
/// authenticator) extend <see cref="StepClaimingMethods"/> with their store methods;
/// this guard then holds them to the same condition. Switching the factor on and
/// off claims the step inside its own statement — the enable's A3d update and the
/// disable's DELETE — under the very condition the step claim uses.
/// </remarks>
public class TotpReplayGuardTests
{
    private const long Step = 59_313_872;

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    // The condition every statement that accepts a TOTP code carries: the step must
    // be newer than the last one accepted (while the rollout switch is on).
    private const string StepIsNewer = "(@RejectReused = 0 OR LastUsedTimeStep IS NULL OR LastUsedTimeStep < @Step)";

    // The step claim, normalized — the one statement SecondFactorAtomicitySqlTests
    // pins in full.
    private const string StepClaim =
        "UPDATE dbo.TwoFactorAuth SET FailedAttempts = 0, LockedUntil = NULL, LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), "
        + "LastUsedTimeStep = CASE WHEN LastUsedTimeStep >= @Step THEN LastUsedTimeStep ELSE @Step END "
        + "OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND (@RejectReused = 0 OR LastUsedTimeStep IS NULL OR LastUsedTimeStep < @Step)";

    // Switching the factor on (A3d) and off with a TOTP code: whole statements,
    // normalized, each claiming the step under the shared condition.
    private const string EnableClaim =
        "UPDATE dbo.TwoFactorAuth SET IsEnabled = 1, EnabledAt = SYSUTCDATETIME(), RecoveryCodes = @RecoveryCodes, "
        + "FailedAttempts = 0, LockedUntil = NULL, LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), "
        + "LastUsedTimeStep = CASE WHEN LastUsedTimeStep >= @Step THEN LastUsedTimeStep ELSE @Step END "
        + "OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 0 AND SecretKey = @SecretSeen AND " + StepIsNewer;

    private const string DisableClaim =
        "DELETE FROM dbo.TwoFactorAuth OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND " + StepIsNewer;

    /// <summary>
    /// The store methods that accept a TOTP code, each run with one, and the whole
    /// statement each must send to claim the code's step.
    /// </summary>
    private static readonly Dictionary<string, (Func<TwoFactorStateStore, Task<LoginCommitOutcome>> Run, string Claim)> StepClaimingMethods = new()
    {
        [nameof(ITwoFactorStateStore.TryCommitLoginAsync)] = (store =>
            store.TryCommitLoginAsync(Guid.NewGuid(), Guid.NewGuid(), SecondFactorProof.Totp(Step), true, CancellationToken.None), StepClaim),
        [nameof(ITwoFactorStateStore.TryClaimTotpStepAsync)] = (store =>
            store.TryClaimTotpStepAsync(Guid.NewGuid(), Step, true, CancellationToken.None), StepClaim),
        [nameof(ITwoFactorStateStore.TryEnableAsync)] = (store =>
            store.TryEnableAsync(Guid.NewGuid(), "v2:ciphertext", "[]", Step, true, null, new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None), EnableClaim),
        [nameof(ITwoFactorStateStore.TryDisableAsync)] = (store =>
            store.TryDisableAsync(Guid.NewGuid(), SecondFactorProof.Totp(Step), true, CancellationToken.None), DisableClaim),
        // S08: a step-up inside a signed-in session settles the factor with the same
        // claim a sign-in uses, in the transaction that upgrades the session.
        [nameof(ITwoFactorStateStore.TryCommitStepUpAsync)] = (store =>
            store.TryCommitStepUpAsync(
                Guid.NewGuid(), SecondFactorProof.Totp(Step), true,
                new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None), StepClaim),
        // S08 PR B: new recovery codes, and starting an authenticator replacement,
        // prove the CURRENT factor and settle it with the sign-in's own claim, in
        // the transaction that makes the change.
        [nameof(ITwoFactorStateStore.TryRegenerateCodesAsync)] = (store =>
            store.TryRegenerateCodesAsync(
                Guid.NewGuid(), SecondFactorProof.Totp(Step), true, "[\"h1\"]", "[\"h2\"]", CancellationToken.None), StepClaim),
        [nameof(ITwoFactorStateStore.TryBeginReplacementAsync)] = (store =>
            store.TryBeginReplacementAsync(
                Guid.NewGuid(), SecondFactorProof.Totp(Step), true, "v2:new-secret", CancellationToken.None), StepClaim),
    };

    /// <summary>
    /// The store method that accepts a code from a NEW secret (S08, contract A3f):
    /// no code of that secret was ever accepted, so it does not take the shared
    /// condition — the previous secret's steps say nothing about it. It is held to
    /// its own: the waiting secret must be the one the code was checked against,
    /// and clearing it in the same statement makes the code count once. The step
    /// it writes is what makes the confirming code refused at the next sign-in.
    /// </summary>
    private const string ReplacementConfirm =
        "UPDATE dbo.TwoFactorAuth SET SecretKey = PendingSecretKey, PendingSecretKey = NULL, PendingSecretCreatedAt = NULL, "
        + "RecoveryCodes = @RecoveryCodes, LastUsedTimeStep = @Step, FailedAttempts = 0, LockedUntil = NULL, "
        + "LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME() "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND PendingSecretKey = @PendingSeen "
        + "AND PendingSecretCreatedAt > DATEADD(MINUTE, -@LifetimeMinutes, SYSUTCDATETIME())";

    private static readonly Dictionary<string, (Func<TwoFactorStateStore, Task<LoginCommitOutcome>> Run, string Claim)> NewSecretStepWriters = new()
    {
        [nameof(ITwoFactorStateStore.TryConfirmReplacementAsync)] = (store =>
            store.TryConfirmReplacementAsync(Guid.NewGuid(), "v2:pending-as-read", Step, "[]", CancellationToken.None), ReplacementConfirm),
    };

    /// <summary>
    /// The store methods that never accept a code: a read, the attempt
    /// reservation, and storing a pending secret during setup.
    /// </summary>
    private static readonly string[] NotAcceptingACode =
    [
        nameof(ITwoFactorStateStore.GetSnapshotAsync),
        nameof(ITwoFactorStateStore.HasEnabledFactorAsync),
        nameof(ITwoFactorStateStore.TryReserveAttemptAsync),
        nameof(ITwoFactorStateStore.TryStorePendingSecretAsync),
        // S08 PR B: who holds a platform role without a factor (a read), and an
        // administrator's reset, which removes the factor and checks no code.
        nameof(ITwoFactorStateStore.HasPlatformRoleHolderWithoutFactorAsync),
        nameof(ITwoFactorStateStore.TryResetAsync),
    ];

    /// <summary>
    /// Files that name the verifier or the proof without consuming one: the
    /// verifier, its proof strategies and their declarations, which check a code
    /// and commit nothing; the proof itself and the store that commits it; and
    /// the composition root, which only registers them.
    /// </summary>
    private static readonly string[] NotProofConsumers =
    [
        "ISecondFactorVerifier.cs",
        "ISecondFactorProofStrategy.cs",
        "SecondFactorVerifier.cs",
        "SecondFactorReservation.cs",
        "TotpProofStrategy.cs",
        "RecoveryCodeProofStrategy.cs",
        "SecondFactorProof.cs",
        "ITwoFactorStateStore.cs",
        "TwoFactorStateStore.cs",
        "Program.cs",
    ];

    /// <summary>The TOTP primitive's declaration and implementation, not check sites.</summary>
    private static readonly string[] TotpPrimitive = ["ITotpService.cs", "TotpService.cs"];

    [Fact]
    public async Task EveryTotpConsumer_CommitsThroughTheStepClaim()
    {
        // 1. Every store method is accounted for, so a new way to write the factor
        //    cannot arrive without saying whether it accepts a code.
        typeof(ITwoFactorStateStore).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(StepClaimingMethods.Keys.Concat(NewSecretStepWriters.Keys).Concat(NotAcceptingACode),
                "a new ITwoFactorStateStore method must be listed here: as one that accepts a TOTP code, and so claims its step, or as one that does not");

        // 2. Every store method that accepts a code claims its step: the whole
        //    statement, under the shared condition.
        foreach (var (name, (run, claim)) in StepClaimingMethods)
        {
            var db = new RecordingDbConnectionFactory(
                affectedRows: 1,
                rowFor: command => command.CommandText.Contains("OUTPUT deleted", StringComparison.Ordinal)
                    ? new { LastUsedTimeStep = (long?)null }
                    : null);

            await run(new TwoFactorStateStore(db));

            claim.Should().EndWith(StepIsNewer);
            db.Commands.Select(Sql).Should().Contain(claim,
                $"{name} accepts a TOTP code, so it must claim the code's time step under the shared condition");
        }

        // 2b. The method that accepts a code from a new secret writes that code's
        //     step in the statement that swaps the secret in, and only for the
        //     waiting secret the code was checked against.
        foreach (var (name, (run, claim)) in NewSecretStepWriters)
        {
            var db = new RecordingDbConnectionFactory(affectedRows: 1);

            await run(new TwoFactorStateStore(db));

            claim.Should().Contain("LastUsedTimeStep = @Step").And.Contain("PendingSecretKey = @PendingSeen");
            db.Commands.Select(Sql).Should().Contain(claim,
                $"{name} accepts a code from a new secret, so it must write that code's step with the swap");
        }

        // Every production project, not one layer: a check added in an endpoint
        // or in Infrastructure is as much a door as one in a handler.
        var production = ApiSourceScan.ProductionSources()
            .Select(source => (Name: Path.GetFileName(source.File), source.Source))
            .ToList();
        production.Should().Contain(file => file.Name == "TotpProofStrategy.cs",
            "the scan must have found the solution's sources");

        // 3. Every file that checks a TOTP code is known: only the proof strategy,
        //    which hands the matched step to a commit in a proof. Sign-in, switching
        //    the factor on and off, and account recovery all check through it.
        var checkSites = production
            .Where(file => !TotpPrimitive.Contains(file.Name))
            .Where(file => Regex.IsMatch(file.Source, @"\.ValidateCode\("))
            .ToList();
        checkSites.Select(file => file.Name).Should().BeEquivalentTo(
            ["TotpProofStrategy.cs"],
            "a new place that checks a TOTP code must go through the verifier and claim its step, not check one itself");

        // 4. Every consumer of a second-factor proof commits it through a method
        //    that claims the step.
        var proofConsumers = production
            .Where(file => !NotProofConsumers.Contains(file.Name))
            .Where(file => Regex.IsMatch(file.Source, @"\b(ISecondFactorVerifier|SecondFactorProof)\b"))
            .ToList();
        proofConsumers.Should().NotBeEmpty("sign-in consumes a proof today");

        proofConsumers.Select(file => file.Name).Should().Contain(
            ["VerifyTwoFactorLoginCommandHandler.cs", "EnableTwoFactorCommandHandler.cs", "DisableTwoFactorCommandHandler.cs", "AccountDeletionRecoverer.cs", "StepUpTwoFactorCommandHandler.cs",
             "RegenerateRecoveryCodesCommandHandler.cs", "BeginAuthenticatorReplacementCommandHandler.cs", "ConfirmAuthenticatorReplacementCommandHandler.cs"]);

        foreach (var file in proofConsumers)
        {
            StepClaimingMethods.Keys.Concat(NewSecretStepWriters.Keys).Should().Contain(
                method => file.Source.Contains($"{method}(", StringComparison.Ordinal),
                $"{file.Name} consumes a second-factor proof, so it must commit it through a method that claims the step");

            // 5. ...and never settles the factor some other way: the whole-row
            //    repository, which writes back what it read, is out of their reach.
            file.Source.Should().NotContain("ITwoFactorAuthRepository",
                $"{file.Name} consumes a second-factor proof, so it must not delete or rewrite the factor row outside the store");
            file.Source.Should().NotMatchRegex(@"\.DeleteAsync\(",
                $"{file.Name} must remove a factor only through the store's conditional DELETE");
        }
    }
}
