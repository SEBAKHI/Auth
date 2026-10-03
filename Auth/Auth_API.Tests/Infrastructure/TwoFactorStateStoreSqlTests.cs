using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the statements that switch the second factor on and off and replace a
/// pending secret. Each is one conditional statement — or one transaction of them
/// with the account flag — whose affected-row count is the decision, so a request
/// that read the factor a moment ago cannot act on what it read.
/// <para>
/// The test project has no database, so the SQL the store sends and a recording
/// connection are the units under test — until S30b proves the same on SQL Server.
/// Statements are compared whole, with brackets removed and whitespace collapsed:
/// a fragment check keeps passing when an AND becomes an OR anywhere it does not
/// look.
/// </para>
/// </summary>
public class TwoFactorStateStoreSqlTests
{
    private const long Step = 59_313_872;
    private const string SecretSeen = "v2:ciphertext-as-read";
    private const string NewCodes = "[\"h1\",\"h2\"]";
    private const string OldCodes = "[ \"h1\", \"h2\", \"h3\" ]";

    private const string ExpectedEnable =
        "UPDATE dbo.TwoFactorAuth SET IsEnabled = 1, EnabledAt = SYSUTCDATETIME(), RecoveryCodes = @RecoveryCodes, "
        + "FailedAttempts = 0, LockedUntil = NULL, LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME(), "
        + "LastUsedTimeStep = CASE WHEN LastUsedTimeStep >= @Step THEN LastUsedTimeStep ELSE @Step END "
        + "OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 0 AND SecretKey = @SecretSeen "
        + "AND (@RejectReused = 0 OR LastUsedTimeStep IS NULL OR LastUsedTimeStep < @Step)";

    private const string ExpectedRemoveWithTotp =
        "DELETE FROM dbo.TwoFactorAuth OUTPUT deleted.LastUsedTimeStep "
        + "WHERE UserId = @UserId AND IsEnabled = 1 "
        + "AND (@RejectReused = 0 OR LastUsedTimeStep IS NULL OR LastUsedTimeStep < @Step)";

    private const string ExpectedRemoveWithRecoveryCode =
        "DELETE FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND IsEnabled = 1 AND RecoveryCodes = @OldCodes";

    private const string ExpectedAccountFlag =
        "UPDATE dbo.Users SET IsTwoFactorEnabled = @IsTwoFactorEnabled, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId "
        + "WHERE Id = @UserId";

    private const string ExpectedRotate =
        "UPDATE dbo.TwoFactorAuth SET SecretKey = @SecretKey, LastUsedTimeStep = NULL, ModifiedAt = SYSUTCDATETIME() "
        + "WHERE UserId = @UserId AND IsEnabled = 0";

    private const string ExpectedInsert =
        "INSERT INTO dbo.TwoFactorAuth (Id, UserId, SecretKey, IsEnabled, FailedAttempts, CreatedAt) "
        + "VALUES (@Id, @UserId, @SecretKey, 0, 0, SYSUTCDATETIME())";

    private const string ExpectedReadIsEnabled = "SELECT IsEnabled FROM dbo.TwoFactorAuth WHERE UserId = @UserId";

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    private static bool Writes(RecordedCommand command, string table) =>
        Regex.IsMatch(command.CommandText, $@"^\s*(UPDATE|DELETE FROM)\s+\[dbo\]\.\[{table}\]");

    private static bool ReturnsTheStep(RecordedCommand command) =>
        command.CommandText.Contains("OUTPUT deleted.[LastUsedTimeStep]", StringComparison.Ordinal);

    private static bool IsTheRead(RecordedCommand command) =>
        command.CommandText.TrimStart().StartsWith("SELECT [IsEnabled]", StringComparison.Ordinal);

    private static Task<LoginCommitOutcome> Enable(
        TwoFactorStateStore store, Guid userId, bool rejectReused = true, Guid? bindCodeId = null) =>
        store.TryEnableAsync(userId, SecretSeen, NewCodes, Step, rejectReused, bindCodeId, CancellationToken.None);

    // ── A4: the factor row and the account flag in one transaction ──────────

    [Fact]
    public async Task Lifecycle_OneTransaction()
    {
        var userId = Guid.NewGuid();

        // Enable, both statements matched: both inside one transaction, committed.
        var enabled = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);
        (await Enable(new TwoFactorStateStore(enabled), userId)).Should().Be(LoginCommitOutcome.Committed);
        enabled.Commands.Should().HaveCount(2);
        enabled.Commands.Should().OnlyContain(command => command.InTransaction,
            "the factor row and the account flag must change together or not at all");
        Writes(enabled.Commands[0], "TwoFactorAuth").Should().BeTrue("the factor row first, then the account row");
        Writes(enabled.Commands[1], "Users").Should().BeTrue();
        enabled.Commands[1].Parameters["IsTwoFactorEnabled"].Should().Be(true);
        enabled.Transactions.Should().ContainSingle();
        enabled.LastTransaction!.Committed.Should().BeTrue();

        // Disable, both statements matched: likewise.
        var disabled = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) } : null);
        (await new TwoFactorStateStore(disabled).TryDisableAsync(userId, SecondFactorProof.Totp(Step), true, CancellationToken.None))
            .Should().Be(LoginCommitOutcome.Committed);
        disabled.Commands.Should().HaveCount(2);
        disabled.Commands.Should().OnlyContain(command => command.InTransaction);
        Writes(disabled.Commands[0], "TwoFactorAuth").Should().BeTrue();
        Writes(disabled.Commands[1], "Users").Should().BeTrue();
        disabled.Commands[1].Parameters["IsTwoFactorEnabled"].Should().Be(false);
        disabled.LastTransaction!.Committed.Should().BeTrue();

        // The account row matched nothing: the factor change rolls back with it,
        // in either direction — a flag and a row that disagree are never committed.
        foreach (var run in new Func<TwoFactorStateStore, Task<LoginCommitOutcome>>[]
                 {
                     store => Enable(store, userId),
                     store => store.TryDisableAsync(userId, SecondFactorProof.RecoveryCode(OldCodes, NewCodes), true, CancellationToken.None),
                 })
        {
            var lost = new RecordingDbConnectionFactory(
                affectedRows: 1,
                rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null,
                affectedFor: command => Writes(command, "Users") ? 0 : 1);

            (await run(new TwoFactorStateStore(lost))).Should().Be(LoginCommitOutcome.FactorLost);
            lost.Commands.Where(command => Writes(command, "TwoFactorAuth") || Writes(command, "Users"))
                .Should().HaveCount(2).And.OnlyContain(command => command.InTransaction);
            lost.LastTransaction!.Committed.Should().BeFalse();
            lost.LastTransaction.RolledBack.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Lifecycle_FactorRowRefused_NeverTouchesTheFlag_AndNamesTheRefusalAfterTheRollback()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = true } : null);

        var outcome = await new TwoFactorStateStore(db)
            .TryDisableAsync(userId, SecondFactorProof.Totp(Step), true, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.StepReused, "the factor is on, so only the step held it back");
        db.Commands.Should().HaveCount(2, "the refused removal, then the read that names the refusal");
        db.Commands.Should().NotContain(command => Writes(command, "Users"),
            "a removal that matched nothing changes no flag");
        db.LastTransaction!.Committed.Should().BeFalse();
        db.LastTransaction.RolledBack.Should().BeTrue();
        db.Commands[1].InTransaction.Should().BeFalse("the refusal is named once nothing is held");
        Sql(db.Commands[1]).Should().Be(ExpectedReadIsEnabled);
    }

    // ── A3d: switching the factor on ────────────────────────────────────────

    [Fact]
    public async Task Enable_ComparesStoredSecret()
    {
        // The pending row is enabled only while it holds the ciphertext the code
        // was checked against — the text as read: the encryption uses a random
        // nonce, so a re-encryption of the same secret would never compare equal.
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);

        await Enable(new TwoFactorStateStore(db), userId);

        var enable = db.Commands[0];
        Sql(enable).Should().Be(ExpectedEnable);
        enable.Parameters["SecretSeen"].Should().Be(SecretSeen);
        enable.Parameters["UserId"].Should().Be(userId);
        enable.Parameters["RecoveryCodes"].Should().Be(NewCodes);
        enable.Parameters["Step"].Should().Be(Step, "the code that switched the factor on is claimed in the same statement");
        enable.Parameters["RejectReused"].Should().Be(true);
        Sql(db.Commands[1]).Should().Be(ExpectedAccountFlag);
    }

    [Theory]
    [InlineData(true, LoginCommitOutcome.AlreadyEnabled)]
    [InlineData(false, LoginCommitOutcome.FactorLost)]
    public async Task Enable_Refused_IsNamedByAReadAfterTheRollback(bool isEnabled, LoginCommitOutcome expected)
    {
        // A concurrent enable won (the factor is on), or the pending secret was
        // replaced (it is off but no longer the one seen): nothing was written.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = isEnabled } : null);

        var outcome = await Enable(new TwoFactorStateStore(db), Guid.NewGuid());

        outcome.Should().Be(expected);
        db.Commands.Should().HaveCount(2);
        db.Commands.Should().NotContain(command => Writes(command, "Users"));
        db.LastTransaction!.RolledBack.Should().BeTrue();
        db.Commands[1].InTransaction.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, LoginCommitOutcome.Committed)]
    [InlineData(Step, LoginCommitOutcome.ReuseAccepted)]
    public async Task Enable_SwitchOff_ReportsAReuseItLetThrough(long? stepBefore, LoginCommitOutcome expected)
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = stepBefore } : null);

        var outcome = await Enable(new TwoFactorStateStore(db), Guid.NewGuid(), rejectReused: false);

        outcome.Should().Be(expected);
        db.Commands[0].Parameters["RejectReused"].Should().Be(false);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    // ── X02 PR B: the emailed code that a first factor needs ────────────────

    private const string ExpectedBindCodeConsume =
        "UPDATE dbo.TwoFactorBindCodes SET UsedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount - 1 "
        + "WHERE Id = @Id AND UsedAt IS NULL";

    [Fact]
    public async Task Enable_WithBindCode_ConsumesTheCodeFirst_InsideTheTransaction()
    {
        // One code binds at most one factor, and only a bind that commits spends
        // it: the code row is the first statement of the very transaction that
        // switches the factor on, before the factor row — the order a sign-in
        // takes its challenge in.
        var userId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);

        var outcome = await Enable(new TwoFactorStateStore(db), userId, bindCodeId: codeId);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(3);
        db.Commands.Should().OnlyContain(command => command.InTransaction,
            "the code, the factor row and the account flag change together or not at all");
        Sql(db.Commands[0]).Should().Be(ExpectedBindCodeConsume);
        db.Commands[0].Parameters["Id"].Should().Be(codeId);
        Sql(db.Commands[1]).Should().Be(ExpectedEnable);
        Sql(db.Commands[2]).Should().Be(ExpectedAccountFlag);
        db.Transactions.Should().ContainSingle();
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_BindCodeSpentFirst_RollsBack_WithoutNamingTheFactor()
    {
        // A concurrent request spent the code first. Nothing is written, and the
        // factor is not read: naming it would answer "set up again" or "already
        // on" to what is a code problem.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = false } : null,
            affectedFor: command => Writes(command, "TwoFactorBindCodes") ? 0 : 1);

        var outcome = await Enable(new TwoFactorStateStore(db), Guid.NewGuid(), bindCodeId: Guid.NewGuid());

        outcome.Should().Be(LoginCommitOutcome.ChallengeLost);
        db.Commands.Should().ContainSingle("the refused consumption is the only statement")
            .Which.InTransaction.Should().BeTrue();
        db.Commands.Should().NotContain(command => IsTheRead(command));
        db.Commands.Should().NotContain(command => Writes(command, "TwoFactorAuth") || Writes(command, "Users"));
        db.LastTransaction!.RolledBack.Should().BeTrue();
        db.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_WithBindCode_FactorRefused_RollsTheCodeBackUnspent()
    {
        // The code was consumed, then the factor row matched nothing (another tab
        // won, or the secret was replaced): the rollback gives the code back, so
        // the user can still use it, and the refusal is named after it.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = true } : null);

        var outcome = await Enable(new TwoFactorStateStore(db), Guid.NewGuid(), bindCodeId: Guid.NewGuid());

        outcome.Should().Be(LoginCommitOutcome.AlreadyEnabled);
        Sql(db.Commands[0]).Should().Be(ExpectedBindCodeConsume);
        db.Commands[0].InTransaction.Should().BeTrue();
        db.Commands.Should().NotContain(command => Writes(command, "Users"));
        db.LastTransaction!.RolledBack.Should().BeTrue("the consumption is undone with the refused bind");
        db.LastTransaction.Committed.Should().BeFalse();
        db.Commands[^1].InTransaction.Should().BeFalse("the refusal is named once nothing is held");
    }

    [Fact]
    public async Task Enable_WithoutBindCode_TouchesNoCode()
    {
        // Email off, or the switch: the bind needs no code and none is consumed.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);

        (await Enable(new TwoFactorStateStore(db), Guid.NewGuid())).Should().Be(LoginCommitOutcome.Committed);

        db.Commands.Should().NotContain(command => command.CommandText.Contains("TwoFactorBindCodes", StringComparison.Ordinal));
    }

    // ── Switching the factor off ────────────────────────────────────────────

    [Fact]
    public async Task Disable_Totp_RemovesOnlyWhileTheStepIsNewer()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) } : null);

        await new TwoFactorStateStore(db).TryDisableAsync(userId, SecondFactorProof.Totp(Step), true, CancellationToken.None);

        var remove = db.Commands[0];
        Sql(remove).Should().Be(ExpectedRemoveWithTotp);
        remove.Parameters["UserId"].Should().Be(userId);
        remove.Parameters["Step"].Should().Be(Step);
        remove.Parameters["RejectReused"].Should().Be(true);
        Sql(db.Commands[1]).Should().Be(ExpectedAccountFlag);
        db.Commands[1].Parameters["UserId"].Should().Be(userId);
    }

    [Fact]
    public async Task Disable_RecoveryCode_RemovesOnlyWhileTheSetIsTheOneChecked()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await new TwoFactorStateStore(db)
            .TryDisableAsync(userId, SecondFactorProof.RecoveryCode(OldCodes, NewCodes), true, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        var remove = db.Commands[0];
        Sql(remove).Should().Be(ExpectedRemoveWithRecoveryCode);
        remove.Parameters["OldCodes"].Should().Be(OldCodes, "the comparison uses the text exactly as loaded");
        remove.Parameters.Keys.Should().NotContain("Step", "a recovery code has no time step");
    }

    [Theory]
    [InlineData(true, true, LoginCommitOutcome.StepReused)]
    [InlineData(true, false, LoginCommitOutcome.FactorLost)]
    [InlineData(false, true, LoginCommitOutcome.RecoveryCodesChanged)]
    [InlineData(false, false, LoginCommitOutcome.FactorLost)]
    public async Task Disable_Refused_IsNamedByAReadAfterTheRollback(bool totp, bool isEnabled, LoginCommitOutcome expected)
    {
        var proof = totp ? SecondFactorProof.Totp(Step) : SecondFactorProof.RecoveryCode(OldCodes, NewCodes);
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = isEnabled } : null);

        var outcome = await new TwoFactorStateStore(db).TryDisableAsync(Guid.NewGuid(), proof, true, CancellationToken.None);

        outcome.Should().Be(expected);
        db.Commands.Should().NotContain(command => Writes(command, "Users"));
        db.LastTransaction!.RolledBack.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, LoginCommitOutcome.Committed)]
    [InlineData(Step, LoginCommitOutcome.ReuseAccepted)]
    [InlineData(Step + 1, LoginCommitOutcome.ReuseAccepted)]
    public async Task Disable_SwitchOff_ReportsAReuseItLetThrough(long? stepBefore, LoginCommitOutcome expected)
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = stepBefore } : null);

        var outcome = await new TwoFactorStateStore(db)
            .TryDisableAsync(Guid.NewGuid(), SecondFactorProof.Totp(Step), false, CancellationToken.None);

        outcome.Should().Be(expected);
        db.Commands[0].Parameters["RejectReused"].Should().Be(false);
    }

    // ── A3e: replacing a pending secret ────────────────────────────────────

    [Fact]
    public async Task Rotate_HasNoDelete()
    {
        // The pending row is rotated in place: its failure count and lock stay, so
        // starting setup again does not clear what guessing at enable earned. An
        // enabled factor matches nothing.
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var stored = await new TwoFactorStateStore(db).TryStorePendingSecretAsync(userId, "v2:new", CancellationToken.None);

        stored.Should().BeTrue();
        db.Commands.Should().ContainSingle("one conditional statement decides");
        Sql(db.Commands[0]).Should().Be(ExpectedRotate);
        db.Commands[0].Parameters["SecretKey"].Should().Be("v2:new");
        db.Commands[0].Parameters["UserId"].Should().Be(userId);
        db.Commands.Should().NotContain(command => command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rotate_EnabledFactor_WritesNothing()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = true } : null);

        var stored = await new TwoFactorStateStore(db).TryStorePendingSecretAsync(Guid.NewGuid(), "v2:new", CancellationToken.None);

        stored.Should().BeFalse("the factor is in use; setup never replaces its secret");
        db.Commands.Select(Sql).Should().Equal(ExpectedRotate, ExpectedReadIsEnabled);
    }

    [Fact]
    public async Task Rotate_NoRow_InsertsThePendingFactor()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null, affectedFor: command =>
            command.CommandText.Contains("INSERT", StringComparison.Ordinal) ? 1 : 0);

        var stored = await new TwoFactorStateStore(db).TryStorePendingSecretAsync(userId, "v2:new", CancellationToken.None);

        stored.Should().BeTrue();
        db.Commands.Select(Sql).Should().Equal(ExpectedRotate, ExpectedReadIsEnabled, ExpectedInsert);
        db.Commands[2].Parameters["UserId"].Should().Be(userId);
        db.Commands[2].Parameters["SecretKey"].Should().Be("v2:new");
    }

    [Theory]
    [InlineData(2601, 1, true)]
    [InlineData(2627, 1, true)]
    [InlineData(2627, 0, false)]
    public async Task Rotate_InsertLosesToAConcurrentSetup_RotatesOnce(int sqlError, int secondRotation, bool expected)
    {
        // Another setup inserted the user's row between the read and the insert: the
        // unique constraint refuses this one, which takes the row over by rotating it
        // once — or finds it enabled meanwhile, and writes nothing.
        var rotations = 0;
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => null,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(sqlError)
                : null,
            affectedFor: command => command.CommandText.Contains("[SecretKey] = @SecretKey", StringComparison.Ordinal)
                ? (rotations++ == 0 ? 0 : secondRotation)
                : 0);

        var stored = await new TwoFactorStateStore(db).TryStorePendingSecretAsync(Guid.NewGuid(), "v2:new", CancellationToken.None);

        stored.Should().Be(expected);
        db.Commands.Select(Sql).Should().Equal(ExpectedRotate, ExpectedReadIsEnabled, ExpectedInsert, ExpectedRotate);
    }

    [Fact]
    public async Task Rotate_OtherInsertFailure_Propagates()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => null,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(547)
                : null);

        var act = () => new TwoFactorStateStore(db).TryStorePendingSecretAsync(Guid.NewGuid(), "v2:new", CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }
}
