using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the statements that make second-factor guessing limits and single-use
/// codes atomic.
/// <para>
/// Each limit is a condition inside the statement that counts, and each single use
/// a condition inside the statement that consumes. Losing any one condition
/// silently reopens the race in which a burst of concurrent guesses all pass a
/// check made on what they read. The test project has no database, so the SQL the
/// repositories send and a recording connection are the units under test — until
/// S30b proves the same on SQL Server under real concurrency. The SQL is compared
/// with its brackets removed and its whitespace collapsed.
/// </para>
/// </summary>
public class SecondFactorAtomicitySqlTests
{
    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    /// <summary>One affected-row count per command, in the order the commands run; 1 after that.</summary>
    private static Func<RecordedCommand, int> Answers(params int[] counts)
    {
        var queue = new Queue<int>(counts);
        return _ => queue.Count > 0 ? queue.Dequeue() : 1;
    }

    // ── A1: the challenge reservation ──────────────────────────────────────

    [Fact]
    public async Task ChallengeReserve_IsConditional()
    {
        var challengeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new { AttemptCount = 3 });

        var reserved = await new TwoFactorChallengeRepository(db)
            .TryReserveAttemptAsync(challengeId, TwoFactorChallenge.MaxAttempts, CancellationToken.None);

        reserved.Should().Be(3, "the count comes back through OUTPUT, from the statement that raised it");
        db.Commands.Should().ContainSingle();

        var sql = Sql(db.LastCommand!);
        sql.Should().StartWith("UPDATE dbo.TwoFactorChallenges SET AttemptCount = AttemptCount + 1");
        sql.Should().Contain("OUTPUT inserted.AttemptCount");
        sql.Should().Contain("WHERE Id = @Id");
        sql.Should().Contain("UsedAt IS NULL", "a consumed challenge admits no further attempt");
        sql.Should().Contain("ExpiresAt > SYSUTCDATETIME()", "an expired challenge admits no further attempt");
        sql.Should().Contain("AttemptCount < @MaxAttempts",
            "the allowance must be a condition of the counting statement, or concurrent guesses all pass it");
        db.LastCommand!.Parameters["Id"].Should().Be(challengeId);
        db.LastCommand.Parameters["MaxAttempts"].Should().Be(TwoFactorChallenge.MaxAttempts);
    }

    [Fact]
    public async Task ChallengeReserve_RefusedWhenNoRowMatches()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        var reserved = await new TwoFactorChallengeRepository(db)
            .TryReserveAttemptAsync(Guid.NewGuid(), TwoFactorChallenge.MaxAttempts, CancellationToken.None);

        reserved.Should().BeNull("no row came back, so no attempt was counted and none may be checked");
    }

    // ── A2: the account reservation ────────────────────────────────────────

    [Fact]
    public async Task AccountReserve_LocksInSameStatement()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => new { FailedAttempts = TwoFactorAuth.MaxFailedAttempts, LockedUntil = (DateTime?)DateTime.UtcNow.AddMinutes(15) });

        var reserved = await new TwoFactorStateStore(db).TryReserveAttemptAsync(userId, CancellationToken.None);

        reserved.Should().Be(TwoFactorAuth.MaxFailedAttempts);
        db.Commands.Should().ContainSingle("the count and the lock must move in one statement");

        var sql = Sql(db.LastCommand!);
        sql.Should().StartWith("UPDATE dbo.TwoFactorAuth SET FailedAttempts = FailedAttempts + 1");
        sql.Should().Contain(
            "LockedUntil = CASE WHEN FailedAttempts + 1 >= @MaxAttempts THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()) ELSE LockedUntil END",
            "the request that reaches the maximum must lock the factor in the statement that counts it");
        sql.Should().Contain("OUTPUT inserted.FailedAttempts, inserted.LockedUntil");
        sql.Should().Contain("WHERE UserId = @UserId AND (LockedUntil IS NULL OR LockedUntil <= SYSUTCDATETIME())",
            "a locked factor must match nothing, so no request past the maximum is counted or checked");
        db.LastCommand!.Parameters["UserId"].Should().Be(userId);
        db.LastCommand.Parameters["MaxAttempts"].Should().Be(5);
        db.LastCommand.Parameters["LockoutMinutes"].Should().Be(15);
    }

    [Fact]
    public async Task AccountReserve_RefusedWhenLocked()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        var reserved = await new TwoFactorStateStore(db).TryReserveAttemptAsync(Guid.NewGuid(), CancellationToken.None);

        reserved.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_KeepsTheStoredTextUntouched()
    {
        // The recovery commit compares the stored set against the text this read
        // returned, so the read must hand it over byte for byte — and the secret
        // must stay the ciphertext a later write can compare against.
        var userId = Guid.NewGuid();
        const string storedCodes = "[ \"h1\", \"h2\" ]";
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => new
            {
                UserId = userId,
                SecretKey = "v2:ciphertext",
                RecoveryCodes = storedCodes,
                IsEnabled = true,
                FailedAttempts = 2,
                LockedUntil = (DateTime?)null
            });

        var snapshot = await new TwoFactorStateStore(db).GetSnapshotAsync(userId, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.ProtectedSecretKey.Should().Be("v2:ciphertext");
        snapshot.RecoveryCodes.Should().Be(storedCodes);
        snapshot.IsEnabled.Should().BeTrue();
        snapshot.FailedAttempts.Should().Be(2);
        Sql(db.LastCommand!).Should().Contain("FROM dbo.TwoFactorAuth WHERE UserId = @UserId");
    }

    // ── A3a + settle / A3c, A4: the login commit ───────────────────────────

    [Fact]
    public async Task LoginCommit_OneTransaction()
    {
        var challengeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // (1, 1): the challenge is consumed and the factor settled — one commit.
        var won = new RecordingDbConnectionFactory(affectedRows: 0, affectedFor: Answers(1, 1));
        var committed = await new TwoFactorStateStore(won)
            .TryCommitLoginAsync(challengeId, userId, SecondFactorProof.Totp(), CancellationToken.None);

        committed.Should().Be(LoginCommitOutcome.Committed);
        won.Commands.Should().HaveCount(2);
        won.Commands.Should().OnlyContain(command => command.InTransaction,
            "the consumption and the settlement must stand or fall together");
        won.Transactions.Should().ContainSingle();
        won.LastTransaction!.Committed.Should().BeTrue();
        won.LastTransaction.RolledBack.Should().BeFalse();

        // (1, 0): the factor refused — the consumption is rolled back with it.
        var lost = new RecordingDbConnectionFactory(affectedRows: 0, affectedFor: Answers(1, 0));
        var refused = await new TwoFactorStateStore(lost)
            .TryCommitLoginAsync(challengeId, userId, SecondFactorProof.Totp(), CancellationToken.None);

        refused.Should().Be(LoginCommitOutcome.FactorLost);
        lost.Commands.Should().HaveCount(2, "a factor that refused releases nothing");
        lost.Commands.Should().OnlyContain(command => command.InTransaction);
        lost.LastTransaction!.Committed.Should().BeFalse();
        lost.LastTransaction.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task LoginCommit_SettleThrows_NeverCommits()
    {
        // Fail-closed when the settle faults (deadlock victim, timeout): the
        // transaction is never committed — the using-block rolls it back on
        // disposal — and the lost-challenge release never runs, so nothing leaks.
        // A catch that swallowed the fault and reached Commit() would turn this red.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("TwoFactorAuth", StringComparison.Ordinal)
                ? new TimeoutException("settle faulted")
                : null);

        var act = () => new TwoFactorStateStore(db)
            .TryCommitLoginAsync(Guid.NewGuid(), Guid.NewGuid(), SecondFactorProof.Totp(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        db.LastTransaction!.Committed.Should().BeFalse("an uncommitted transaction is rolled back on disposal");
        db.Commands.Should().HaveCount(2, "the consume then the faulting settle; no release ran");
    }

    [Fact]
    public async Task LoginCommit_ConsumeDecrements()
    {
        var challengeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorStateStore(db)
            .TryCommitLoginAsync(challengeId, userId, SecondFactorProof.Totp(), CancellationToken.None);

        var consume = Sql(db.Commands[0]);
        consume.Should().StartWith("UPDATE dbo.TwoFactorChallenges SET UsedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount - 1",
            "a correct code gives its reservation back, so at rest the count is the rejected codes the history shows");
        consume.Should().Contain("WHERE Id = @Id AND UsedAt IS NULL", "only one request may consume a challenge");
        db.Commands[0].Parameters["Id"].Should().Be(challengeId);

        var settle = Sql(db.Commands[1]);
        settle.Should().StartWith("UPDATE dbo.TwoFactorAuth SET FailedAttempts = 0, LockedUntil = NULL, LastUsedAt = SYSUTCDATETIME()");
        settle.Should().Contain("WHERE UserId = @UserId AND IsEnabled = 1",
            "a factor switched off after the code was checked settles nothing");
        settle.Should().NotContain("RecoveryCodes", "a TOTP proof leaves the recovery codes alone");
        db.Commands[1].Parameters["UserId"].Should().Be(userId);
    }

    [Fact]
    public async Task LoginCommit_LostChallengeReleases()
    {
        var challengeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, affectedFor: Answers(0));

        var outcome = await new TwoFactorStateStore(db)
            .TryCommitLoginAsync(challengeId, Guid.NewGuid(), SecondFactorProof.Totp(), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.ChallengeLost);
        db.LastTransaction!.Committed.Should().BeFalse();
        db.LastTransaction.RolledBack.Should().BeTrue();

        db.Commands.Should().HaveCount(2, "the factor is never settled for a lost challenge; the reservation is released");
        db.Commands[0].InTransaction.Should().BeTrue();

        var release = db.Commands[1];
        release.InTransaction.Should().BeFalse("the release runs after the rollback, outside the transaction");
        var sql = Sql(release);
        sql.Should().StartWith("UPDATE dbo.TwoFactorChallenges SET AttemptCount = AttemptCount - 1");
        sql.Should().Contain("UsedAt IS NOT NULL", "a live challenge keeps every reservation");
        sql.Should().Contain("AttemptCount > 0");
        release.Parameters["ChallengeId"].Should().Be(challengeId);
    }

    [Fact]
    public async Task RecoveryCommit_ComparesOldSet()
    {
        const string loaded = "[ \"h1\", \"h2\" ]";
        const string remaining = "[\"h2\"]";
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await new TwoFactorStateStore(db).TryCommitLoginAsync(
            Guid.NewGuid(), Guid.NewGuid(), SecondFactorProof.RecoveryCode(loaded, remaining), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        var settle = Sql(db.Commands[1]);
        settle.Should().Contain("RecoveryCodes = @NewCodes");
        settle.Should().Contain("AND RecoveryCodes = @OldCodes",
            "the code is spent only while the stored set is still the one it was checked against");
        settle.Should().Contain("IsEnabled = 1");
        db.Commands[1].Parameters["OldCodes"].Should().Be(loaded, "the comparison must use the text exactly as loaded");
        db.Commands[1].Parameters["NewCodes"].Should().Be(remaining);
    }

    // ── A5: the siblings ───────────────────────────────────────────────────

    private sealed record Sibling(
        string Table,
        Func<IDbConnectionFactory, Guid, Task<int?>> Reserve,
        Func<IDbConnectionFactory, Guid, Task<bool>> Consume);

    private static readonly Dictionary<string, Sibling> Siblings = new()
    {
        ["EmailVerificationTokens"] = new(
            "EmailVerificationTokens",
            (db, id) => new EmailVerificationTokenRepository(db).TryReserveAttemptAsync(id, EmailVerificationToken.MaxAttempts, CancellationToken.None),
            (db, id) => new EmailVerificationTokenRepository(db).TryConsumeAsync(id, CancellationToken.None)),
        ["OwnershipTransferCodes"] = new(
            "OwnershipTransferCodes",
            (db, id) => new OwnershipTransferCodeRepository(db).TryReserveAttemptAsync(id, OwnershipTransferCode.MaxAttempts, CancellationToken.None),
            (db, id) => new OwnershipTransferCodeRepository(db).TryConsumeAsync(id, CancellationToken.None)),
    };

    [Theory]
    [InlineData("EmailVerificationTokens")]
    [InlineData("OwnershipTransferCodes")]
    public async Task SiblingStatements_AreConditional(string table)
    {
        var sibling = Siblings[table];
        var id = Guid.NewGuid();

        var reserveDb = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new { AttemptCount = 2 });
        (await sibling.Reserve(reserveDb, id)).Should().Be(2);
        var reserve = Sql(reserveDb.LastCommand!);
        reserve.Should().StartWith($"UPDATE dbo.{table} SET AttemptCount = AttemptCount + 1");
        reserve.Should().Contain("OUTPUT inserted.AttemptCount");
        reserve.Should().Contain("UsedAt IS NULL");
        reserve.Should().Contain("ExpiresAt > SYSUTCDATETIME()");
        reserve.Should().Contain("AttemptCount < @MaxAttempts");
        reserveDb.LastCommand!.Parameters["Id"].Should().Be(id);
        reserveDb.LastCommand.Parameters["MaxAttempts"].Should().Be(5);

        var refusedDb = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        (await sibling.Reserve(refusedDb, id)).Should().BeNull();

        var consumeDb = new RecordingDbConnectionFactory(affectedRows: 1);
        (await sibling.Consume(consumeDb, id)).Should().BeTrue();
        var consume = Sql(consumeDb.LastCommand!);
        consume.Should().StartWith($"UPDATE dbo.{table} SET UsedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount - 1");
        consume.Should().Contain("WHERE Id = @Id AND UsedAt IS NULL");

        var lostDb = new RecordingDbConnectionFactory(affectedRows: 0);
        (await sibling.Consume(lostDb, id)).Should().BeFalse("only the request that consumed the code may act on it");
    }

    // ── T10: no whole-row write, no unconditional increment ────────────────

    [Fact]
    public void VerifyPath_NoWholeRowWrite()
    {
        var root = ApiSourceScan.SolutionDirectory();
        var verifyPath = new[]
        {
            Path.Combine(root, "Auth.Application", "Features", "Authentication", "VerifyTwoFactorLogin", "VerifyTwoFactorLoginCommandHandler.cs"),
            Path.Combine(root, "Auth.Application", "Features", "Authentication", "Common", "SecondFactorVerifier.cs"),
            Path.Combine(root, "Auth.Application", "Features", "Authentication", "Common", "TotpProofStrategy.cs"),
            Path.Combine(root, "Auth.Application", "Features", "Authentication", "Common", "RecoveryCodeProofStrategy.cs"),
        };

        foreach (var file in verifyPath)
        {
            var source = File.ReadAllText(file);
            source.Should().NotContain("ITwoFactorAuthRepository",
                $"{Path.GetFileName(file)} must change two-factor state only through the conditional statements of ITwoFactorStateStore");
            source.Should().NotContain("UpdateAsync(",
                $"{Path.GetFileName(file)} must not write a row computed from a read");
        }

        // Both the explicit `new SecondFactorReservation(` and a target-typed
        // `SecondFactorReservation r = new(` count: the reservation's constructor
        // is internal, so any Application file could otherwise mint one.
        var mintsReservation = new Regex(
            @"new\s+SecondFactorReservation\s*\(|SecondFactorReservation\s+\w+\s*=\s*new\s*\(");
        var minted = ApiSourceScan.ProductionSources()
            .Where(source => mintsReservation.IsMatch(source.Source))
            .Select(source => Path.GetFileName(source.File))
            .ToList();

        minted.Should().BeEquivalentTo(new[] { "SecondFactorVerifier.cs" },
            "a reservation is the proof that an attempt was counted; only the verifier, which counts it, may create one");
    }

    [Fact]
    public void SecondFactorCheckSites_AreKnown()
    {
        // Every place a TOTP or recovery code is checked, so a new door cannot be
        // added silently without the reserve-before-check guard. The two proof
        // strategies are X08's; the other three check a code without a reservation
        // and are explicitly a later item's job (X02). ITotpService/TotpService are
        // the primitive's declaration and implementation, not a check site.
        var callers = ApiSourceScan.ProductionSources()
            .Where(s => Regex.IsMatch(s.Source, @"\.(ValidateCode|VerifyRecoveryCode)\("))
            .Select(s => Path.GetFileName(s.File))
            .Where(name => name != "ITotpService.cs" && name != "TotpService.cs")
            .OrderBy(name => name)
            .ToList();

        callers.Should().BeEquivalentTo(new[]
        {
            // X08 — reserve-before-check through ISecondFactorVerifier:
            "TotpProofStrategy.cs",
            "RecoveryCodeProofStrategy.cs",
            // Residual, owned by X02 — still check without a reservation/lock/count:
            "EnableTwoFactorCommandHandler.cs",
            "DisableTwoFactorCommandHandler.cs",
            "AccountDeletionRecoverer.cs",
        }, "a new second-factor check site must adopt the reservation (X02 shrinks the residual list), not appear here unnoticed");
    }

    [Theory]
    [InlineData(typeof(ITwoFactorChallengeRepository))]
    [InlineData(typeof(IEmailVerificationTokenRepository))]
    [InlineData(typeof(IOwnershipTransferCodeRepository))]
    public void FoldedRepos_NoUnconditionalIncrement(Type repository)
    {
        var methods = repository.GetMethods().Select(method => method.Name).ToList();

        methods.Should().Contain("TryReserveAttemptAsync");
        methods.Should().NotContain("IncrementAttemptCountAsync",
            "an increment after the check only records attempts; it never refuses the one past the cap");
        methods.Should().NotContain("MarkAsUsedAsync",
            "a consumption whose affected-row count is not returned cannot tell the winner from the loser");
    }
}
