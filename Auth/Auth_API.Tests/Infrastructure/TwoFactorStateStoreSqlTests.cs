using System.Text.RegularExpressions;
using Auth.Domain.Entities;
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

    // S08: the session upgrades, normalized. ISNULL because NULL | @Method is NULL;
    // only the caller's own live row, and the live SSO session the cookie names.
    private const string ExpectedUserSessionUpgrade =
        "UPDATE dbo.UserSessions SET AuthMethods = ISNULL(AuthMethods, 0) | @Method "
        + "WHERE Id = @SessionId AND UserId = @UserId AND EndedAt IS NULL";

    private const string ExpectedIdpSessionUpgrade =
        "UPDATE dbo.IdpSessions SET AuthMethods = ISNULL(AuthMethods, 0) | @Method "
        + "WHERE TokenHash = @IdpTokenHash AND UserId = @UserId AND RevokedAt IS NULL";

    private const string ExpectedHasEnabledFactor =
        "SELECT CAST(CASE WHEN EXISTS ( SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND IsEnabled = 1) THEN 1 ELSE 0 END AS BIT)";

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
        store.TryEnableAsync(
            userId, SecretSeen, NewCodes, Step, rejectReused, bindCodeId,
            new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None);

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
        // S08: the session the code was entered in is upgraded last, inside the same
        // transaction (no SSO cookie here, so no SSO session statement).
        enabled.Commands.Should().HaveCount(3);
        enabled.Commands.Should().OnlyContain(command => command.InTransaction,
            "the factor row and the account flag must change together or not at all");
        Writes(enabled.Commands[0], "TwoFactorAuth").Should().BeTrue("the factor row first, then the account row");
        Writes(enabled.Commands[1], "Users").Should().BeTrue();
        enabled.Commands[1].Parameters["IsTwoFactorEnabled"].Should().Be(true);
        Writes(enabled.Commands[2], "UserSessions").Should().BeTrue("then the session the code was entered in");
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
        // S08 adds the session upgrade as the transaction's last statement.
        db.Commands.Should().HaveCount(4);
        db.Commands.Should().OnlyContain(command => command.InTransaction,
            "the code, the factor row and the account flag change together or not at all");
        Sql(db.Commands[0]).Should().Be(ExpectedBindCodeConsume);
        db.Commands[0].Parameters["Id"].Should().Be(codeId);
        Sql(db.Commands[1]).Should().Be(ExpectedEnable);
        Sql(db.Commands[2]).Should().Be(ExpectedAccountFlag);
        Sql(db.Commands[3]).Should().Be(ExpectedUserSessionUpgrade);
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

    // ── S08: the session upgrades, the factor read, the step-up ─────────────

    private static readonly Guid StepUpSession = Guid.NewGuid();

    private static Task<LoginCommitOutcome> StepUp(
        TwoFactorStateStore store, Guid userId, SecondFactorProof proof, string? idpHash = "idp-hash") =>
        store.TryCommitStepUpAsync(
            userId, proof, true,
            new SessionUpgrade(StepUpSession, idpHash, AuthenticationMethods.From(proof.Method)), CancellationToken.None);

    [Fact]
    public async Task HasEnabledFactor_ReadsTheEnabledRow_NeverTheAccountFlag()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, scalarFor: _ => true);

        (await new TwoFactorStateStore(db).HasEnabledFactorAsync(userId, CancellationToken.None)).Should().BeTrue();

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(ExpectedHasEnabledFactor);
        db.LastCommand!.Parameters["UserId"].Should().Be(userId);
        db.LastCommand.CommandText.Should().NotContain("Users", "the account flag can disagree with the row");
    }

    [Fact]
    public async Task StepUp_Totp_SettlesTheStep_ThenUpgradesBothSessions_InOneTransaction()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) } : null);

        var outcome = await StepUp(new TwoFactorStateStore(db), userId, SecondFactorProof.Totp(Step));

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(3);
        db.Commands.Should().OnlyContain(command => command.InTransaction,
            "the factor and the session change together or not at all");
        Writes(db.Commands[0], "TwoFactorAuth").Should().BeTrue("the factor row first, as every lifecycle transaction takes it");
        ReturnsTheStep(db.Commands[0]).Should().BeTrue("the TOTP step claim, the statement sign-in uses");
        Sql(db.Commands[1]).Should().Be(ExpectedUserSessionUpgrade);
        db.Commands[1].Parameters["SessionId"].Should().Be(StepUpSession);
        db.Commands[1].Parameters["UserId"].Should().Be(userId);
        db.Commands[1].Parameters["Method"].Should().Be(AuthenticationMethods.Totp.Value);
        Sql(db.Commands[2]).Should().Be(ExpectedIdpSessionUpgrade);
        db.Commands[2].Parameters["IdpTokenHash"].Should().Be("idp-hash");
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task StepUp_RecoveryCode_ReplacesTheSetOnlyWhileUnchanged_AndUpgradesWithRecoveryCode()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.RecoveryCode(OldCodes, NewCodes));

        outcome.Should().Be(LoginCommitOutcome.Committed);
        Sql(db.Commands[0]).Should().EndWith("WHERE UserId = @UserId AND IsEnabled = 1 AND RecoveryCodes = @OldCodes");
        db.Commands[0].Parameters["OldCodes"].Should().Be(OldCodes);
        db.Commands[1].Parameters["Method"].Should().Be(AuthenticationMethods.RecoveryCode.Value);
    }

    [Fact]
    public async Task StepUp_SessionEnded_RollsBack_AndSaysSo()
    {
        // The session row must match: a factor settled for a session that ended in
        // the meantime would be a code spent for nothing — and nothing is written.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null,
            affectedFor: command => Writes(command, "UserSessions") ? 0 : 1);

        var outcome = await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.Totp(Step));

        outcome.Should().Be(LoginCommitOutcome.SessionLost);
        db.LastTransaction!.RolledBack.Should().BeTrue();
        db.Commands.Should().NotContain(command => Writes(command, "IdpSessions"));
    }

    [Fact]
    public async Task StepUp_SsoSessionMatchesNothing_StillCommits()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null,
            affectedFor: command => Writes(command, "IdpSessions") ? 0 : 1);

        (await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.Totp(Step)))
            .Should().Be(LoginCommitOutcome.Committed);
        db.LastTransaction!.Committed.Should().BeTrue("the cookie may not reach the API; that never refuses a step-up");
    }

    [Fact]
    public async Task StepUp_ReusedStep_RollsBack_AndIsNamedAfterTheRollback()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = true } : null);

        var outcome = await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.Totp(Step));

        outcome.Should().Be(LoginCommitOutcome.StepReused);
        db.Transactions.Should().ContainSingle().Which.RolledBack.Should().BeTrue();
        db.Commands.Should().NotContain(command => Writes(command, "UserSessions"));
        IsTheRead(db.LastCommand!).Should().BeTrue("the reason is read once nothing is held");
        db.LastCommand!.InTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task StepUp_RecoveryCodeSetChanged_IsNamedRecoveryCodesChanged()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = true } : null,
            affectedFor: command => Writes(command, "TwoFactorAuth") ? 0 : 1);

        (await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.RecoveryCode(OldCodes, NewCodes)))
            .Should().Be(LoginCommitOutcome.RecoveryCodesChanged);
    }

    [Fact]
    public async Task StepUp_FactorGone_IsNamedFactorLost()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = false } : null);

        (await StepUp(new TwoFactorStateStore(db), Guid.NewGuid(), SecondFactorProof.Totp(Step)))
            .Should().Be(LoginCommitOutcome.FactorLost);
    }

    // ── S08 PR B: regenerate (A3g), replace (A3f), the reset, the role-holder read ──

    private const string ExpectedReplaceRecoveryCodes =
        "UPDATE dbo.TwoFactorAuth SET RecoveryCodes = @NewCodes, ModifiedAt = SYSUTCDATETIME() "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND (RecoveryCodes = @OldCodes OR (RecoveryCodes IS NULL AND @OldCodes IS NULL))";

    private const string ExpectedStorePendingReplacement =
        "UPDATE dbo.TwoFactorAuth SET PendingSecretKey = @PendingSecretKey, PendingSecretCreatedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME() "
        + "WHERE UserId = @UserId AND IsEnabled = 1";

    private const string ExpectedConfirmReplacement =
        "UPDATE dbo.TwoFactorAuth SET SecretKey = PendingSecretKey, PendingSecretKey = NULL, PendingSecretCreatedAt = NULL, "
        + "RecoveryCodes = @RecoveryCodes, LastUsedTimeStep = @Step, FailedAttempts = 0, LockedUntil = NULL, "
        + "LastUsedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME() "
        + "WHERE UserId = @UserId AND IsEnabled = 1 AND PendingSecretKey = @PendingSeen "
        + "AND PendingSecretCreatedAt > DATEADD(MINUTE, -@LifetimeMinutes, SYSUTCDATETIME())";

    private const string ExpectedRemoveFactor = "DELETE FROM dbo.TwoFactorAuth WHERE UserId = @UserId";

    private const string ExpectedClearFlagByAdministrator =
        "UPDATE dbo.Users SET IsTwoFactorEnabled = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ResetBy WHERE Id = @UserId";

    private const string ExpectedRoleHolderWithoutFactor =
        "SELECT CAST(CASE WHEN EXISTS ( SELECT 1 FROM dbo.UserRoles ur "
        + "INNER JOIN dbo.Roles r ON r.Id = ur.RoleId "
        + "INNER JOIN dbo.Users u ON u.Id = ur.UserId "
        + "WHERE ur.RoleId = @RoleId "
        + "AND ur.ApplicationId IS NULL AND r.ApplicationId IS NULL "
        + "AND ur.IsActive = 1 AND r.IsActive = 1 "
        + "AND (ur.ExpiresAt IS NULL OR ur.ExpiresAt > GETUTCDATE()) "
        + "AND u.IsDeleted = 0 "
        + "AND NOT EXISTS ( SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = ur.UserId AND IsEnabled = 1)) "
        + "THEN 1 ELSE 0 END AS BIT)";

    [Fact]
    public async Task Regenerate_Totp_ClaimsTheStep_ThenReplacesTheSetSeen_InOneTransaction()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) } : null);

        var outcome = await new TwoFactorStateStore(db).TryRegenerateCodesAsync(
            userId, SecondFactorProof.Totp(Step), true, OldCodes, NewCodes, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(2);
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        ReturnsTheStep(db.Commands[0]).Should().BeTrue("the TOTP claim first");
        Sql(db.Commands[1]).Should().Be(ExpectedReplaceRecoveryCodes);
        db.Commands[1].Parameters["OldCodes"].Should().Be(OldCodes, "a TOTP proof replaces the set as read");
        db.Commands[1].Parameters["NewCodes"].Should().Be(NewCodes);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Regenerate_RecoveryCode_SpendsIt_ThenReplacesTheSetWithoutIt()
    {
        var withoutTheCode = "[\"h2\",\"h3\"]";
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await new TwoFactorStateStore(db).TryRegenerateCodesAsync(
            Guid.NewGuid(), SecondFactorProof.RecoveryCode(OldCodes, withoutTheCode), true, OldCodes, NewCodes, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(2);
        db.Commands[0].Parameters["OldCodes"].Should().Be(OldCodes, "A3c spends the code against the set it was checked against");
        db.Commands[1].Parameters["OldCodes"].Should().Be(withoutTheCode,
            "A3g replaces what A3c left: the set the proof produced, never the one read before");
    }

    [Fact]
    public async Task Regenerate_SetChangedFirst_RollsBackTheClaim_AndSaysSo()
    {
        // The claim matched; the set was replaced by a concurrent request (0 rows).
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) }
                : IsTheRead(command) ? new { IsEnabled = true } : null,
            affectedFor: command => Sql(command) == ExpectedReplaceRecoveryCodes ? 0 : 1);

        var outcome = await new TwoFactorStateStore(db).TryRegenerateCodesAsync(
            Guid.NewGuid(), SecondFactorProof.Totp(Step), true, OldCodes, NewCodes, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.RecoveryCodesChanged);
        db.LastTransaction!.RolledBack.Should().BeTrue("the claim must not stand without the new set");
        db.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task BeginReplacement_ClaimsTheStep_ThenStoresTheWaitingSecret_InOneTransaction()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)(Step - 1) } : null);

        var outcome = await new TwoFactorStateStore(db).TryBeginReplacementAsync(
            Guid.NewGuid(), SecondFactorProof.Totp(Step), true, "v2:new-secret", CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(2);
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        Sql(db.Commands[1]).Should().Be(ExpectedStorePendingReplacement);
        db.Commands[1].Parameters["PendingSecretKey"].Should().Be("v2:new-secret");
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmReplacement_IsOneStatement_OnTheWaitingSecretAsRead_TimedByTheDatabase()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await new TwoFactorStateStore(db).TryConfirmReplacementAsync(
            userId, "v2:pending-as-read", Step, NewCodes, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        var command = db.Commands.Should().ContainSingle().Subject;
        command.InTransaction.Should().BeFalse("one statement needs no transaction (A4)");
        Sql(command).Should().Be(ExpectedConfirmReplacement);
        command.Parameters["PendingSeen"].Should().Be("v2:pending-as-read");
        command.Parameters["Step"].Should().Be(Step, "the NEW secret's step: the confirming code cannot sign in again");
        command.Parameters["LifetimeMinutes"].Should().Be(TwoFactorAuth.PendingReplacementLifetimeMinutes);
    }

    [Theory]
    [InlineData(true, LoginCommitOutcome.ChallengeLost)]
    [InlineData(false, LoginCommitOutcome.FactorLost)]
    public async Task ConfirmReplacement_NoMatch_IsNamedAfterAReadOfTheFactor(bool factorEnabled, LoginCommitOutcome expected)
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: command => IsTheRead(command) ? new { IsEnabled = factorEnabled } : null);

        var outcome = await new TwoFactorStateStore(db).TryConfirmReplacementAsync(
            Guid.NewGuid(), "v2:pending-as-read", Step, NewCodes, CancellationToken.None);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Reset_RemovesTheRowWhateverItHolds_AndClearsTheFlag_InOneTransaction()
    {
        var userId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        (await new TwoFactorStateStore(db).TryResetAsync(userId, actor, CancellationToken.None)).Should().BeTrue();

        db.Commands.Should().HaveCount(2);
        db.Commands.Should().OnlyContain(command => command.InTransaction,
            "the factor row and the account flag change together or not at all");
        Sql(db.Commands[0]).Should().Be(ExpectedRemoveFactor, "no condition on the row's state: a reset removes it whatever it holds");
        Sql(db.Commands[1]).Should().Be(ExpectedClearFlagByAdministrator);
        db.Commands[1].Parameters["ResetBy"].Should().Be(actor);
        db.Transactions.Should().ContainSingle();
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_WithoutAnAccountRow_RollsBack()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            affectedFor: command => Writes(command, "Users") ? 0 : 1);

        (await new TwoFactorStateStore(db).TryResetAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();

        db.LastTransaction!.RolledBack.Should().BeTrue();
        db.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task RoleHolderWithoutFactor_IsOneRead_WithThePlatformTokensPredicates()
    {
        var roleId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, scalarFor: _ => true);

        (await new TwoFactorStateStore(db).HasPlatformRoleHolderWithoutFactorAsync(roleId, CancellationToken.None)).Should().BeTrue();

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(ExpectedRoleHolderWithoutFactor);
        db.LastCommand!.Parameters["RoleId"].Should().Be(roleId);
        // The factor test is the very one HasEnabledFactorAsync runs, correlated.
        ExpectedHasEnabledFactor.Should().Contain("SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND IsEnabled = 1");
        ExpectedRoleHolderWithoutFactor.Should().Contain("SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = ur.UserId AND IsEnabled = 1");
    }

    [Fact]
    public async Task Snapshot_ReadsTheWaitingReplacement()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorStateStore(db).GetSnapshotAsync(Guid.NewGuid(), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[PendingSecretKey], [PendingSecretCreatedAt]");
    }
}
