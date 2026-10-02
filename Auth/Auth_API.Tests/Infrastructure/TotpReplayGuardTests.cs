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
/// Later items that add a way to accept a code (enable and disable transactions,
/// step-up, replacing the authenticator) extend <see cref="StepClaimingMethods"/>
/// with their store methods; this guard then holds them to the same statement.
/// </remarks>
public class TotpReplayGuardTests
{
    private const long Step = 59_313_872;

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    // The step claim, normalized — the one statement SecondFactorAtomicitySqlTests
    // pins in full.
    private const string StepClaim =
        "UPDATE dbo.TwoFactorAuth SET FailedAttempts = 0, LockedUntil = NULL, LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), "
        + "LastUsedTimeStep = CASE WHEN LastUsedTimeStep >= @Step THEN LastUsedTimeStep ELSE @Step END "
        + "OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND (@RejectReused = 0 OR LastUsedTimeStep IS NULL OR LastUsedTimeStep < @Step)";

    /// <summary>The store methods that accept a TOTP code, each run with one.</summary>
    private static readonly Dictionary<string, Func<TwoFactorStateStore, Task<LoginCommitOutcome>>> StepClaimingMethods = new()
    {
        [nameof(ITwoFactorStateStore.TryCommitLoginAsync)] = store =>
            store.TryCommitLoginAsync(Guid.NewGuid(), Guid.NewGuid(), SecondFactorProof.Totp(Step), true, CancellationToken.None),
        [nameof(ITwoFactorStateStore.TryClaimTotpStepAsync)] = store =>
            store.TryClaimTotpStepAsync(Guid.NewGuid(), Step, true, CancellationToken.None),
    };

    /// <summary>The store methods that never accept a code: a read, and the attempt reservation.</summary>
    private static readonly string[] NotAcceptingACode =
    [
        nameof(ITwoFactorStateStore.GetSnapshotAsync),
        nameof(ITwoFactorStateStore.TryReserveAttemptAsync),
    ];

    /// <summary>
    /// The machinery that checks a code and produces a proof, but commits nothing
    /// itself: the verifier, its proof strategies, and their declarations.
    /// </summary>
    private static readonly string[] ProofProducers =
    [
        "ISecondFactorVerifier.cs",
        "ISecondFactorProofStrategy.cs",
        "SecondFactorVerifier.cs",
        "SecondFactorReservation.cs",
        "TotpProofStrategy.cs",
        "RecoveryCodeProofStrategy.cs",
    ];

    [Fact]
    public async Task EveryTotpConsumer_CommitsThroughTheStepClaim()
    {
        // 1. Every store method is accounted for, so a new way to write the factor
        //    cannot arrive without saying whether it accepts a code.
        typeof(ITwoFactorStateStore).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(StepClaimingMethods.Keys.Concat(NotAcceptingACode),
                "a new ITwoFactorStateStore method must be listed here: as one that accepts a TOTP code, and so claims its step, or as one that does not");

        // 2. Every store method that accepts a code runs the step claim.
        foreach (var (name, run) in StepClaimingMethods)
        {
            var db = new RecordingDbConnectionFactory(
                affectedRows: 1,
                rowFor: command => command.CommandText.Contains("OUTPUT deleted", StringComparison.Ordinal)
                    ? new { LastUsedTimeStep = (long?)null }
                    : null);

            await run(new TwoFactorStateStore(db));

            db.Commands.Select(Sql).Should().Contain(StepClaim,
                $"{name} accepts a TOTP code, so it must claim the code's time step with the shared statement");
        }

        var application = ApiSourceScan.ProductionSources()
            .Where(source => source.File.Contains(
                $"{Path.DirectorySeparatorChar}Auth.Application{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(source => (Name: Path.GetFileName(source.File), source.Source))
            .ToList();
        application.Should().NotBeEmpty("the scan must have found the Application sources");

        // 3. Every Application file that checks a TOTP code is known. The sign-in
        //    strategy hands its step to the login commit in a proof; the three
        //    older paths claim the step themselves.
        var checkSites = application
            .Where(file => Regex.IsMatch(file.Source, @"\.ValidateCode\("))
            .ToList();
        checkSites.Select(file => file.Name).Should().BeEquivalentTo(
            ["TotpProofStrategy.cs", "DisableTwoFactorCommandHandler.cs", "EnableTwoFactorCommandHandler.cs", "AccountDeletionRecoverer.cs"],
            "a new place that checks a TOTP code must claim its step, and be listed here");

        foreach (var file in checkSites.Where(file => file.Name != "TotpProofStrategy.cs"))
        {
            file.Source.Should().Contain("TryClaimTotpStepAsync(",
                $"{file.Name} checks a TOTP code, so it must claim the code's step — never settle the factor some other way");
        }

        // 4. Every Application consumer of a second-factor proof commits it through
        //    a method that claims the step.
        var proofConsumers = application
            .Where(file => !ProofProducers.Contains(file.Name))
            .Where(file => Regex.IsMatch(file.Source, @"\b(ISecondFactorVerifier|SecondFactorProof)\b"))
            .ToList();
        proofConsumers.Should().NotBeEmpty("sign-in consumes a proof today");

        foreach (var file in proofConsumers)
        {
            StepClaimingMethods.Keys.Should().Contain(
                method => file.Source.Contains($"{method}(", StringComparison.Ordinal),
                $"{file.Name} consumes a second-factor proof, so it must commit it through a method that claims the step");
        }
    }
}
