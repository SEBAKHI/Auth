using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The statements of the emailed second factor (S24): the one statement that reserves a
/// send under every limit, the emailed code's own failure counter, its commit, and the
/// purpose every statement on the challenge table names. The test project has no
/// database, so the SQL and a recording connection are the units under test; the send
/// reservation's behaviour under concurrency is proved separately on a real SQL Server.
/// Statements are compared whole, or by the exact condition that makes them safe, with
/// brackets removed and whitespace collapsed.
/// </summary>
public class TwoFactorEmailSqlTests
{
    private const string CodesSeen = "mac-1;mac-2";

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    private static EmailCodeSendReservation Send(TwoFactorChallengePurpose purpose = TwoFactorChallengePurpose.SignIn, int? hard = 100) =>
        new(Guid.NewGuid(), purpose, "mac-3", LifetimeMinutes: 10, RequireBoundFactor: true, hard,
            WindowSeconds: 60, WindowMax: 3, DailyMax: TwoFactorAuth.MaxEmailCodesPerDay);

    // ── The send: one statement, every limit a condition of it ──

    [Fact]
    public async Task SendReservation_IsOneStatement_WithEveryLimitAsACondition()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var receipt = await new TwoFactorChallengeRepository(db).TryReserveEmailCodeSendAsync(Send(), CancellationToken.None);

        receipt.Should().BeNull("no row came back: a limit refused the send");
        db.Commands.Should().ContainSingle("the limits are conditions of the statement that writes, never a read before it");
        var sql = Sql(db.Commands[0]);

        // The challenge: live, of the purpose, under its three sends, with an attempt left.
        sql.Should().Contain("WHERE c.Id = @ChallengeId AND c.Purpose = @Purpose AND c.UsedAt IS NULL AND c.ExpiresAt > @Now "
                             + "AND c.EmailCodeSendCount < @PerChallengeMax AND c.AttemptCount < @MaxAttempts AND (@RequireBoundFactor");
        // The factor: bound and not locked, for a while or for good.
        sql.Should().Contain("AND (@RequireBoundFactor = 0 OR EXISTS ( SELECT 1 FROM dbo.TwoFactorAuth f WHERE f.UserId = c.UserId "
                             + "AND f.EmailCodeEnabledAt IS NOT NULL AND ISNULL(f.EmailLockedUntil, @Now) <= @Now "
                             + "AND (@Hard IS NULL OR f.EmailFailedAttempts < @Hard)))");
        // The account: both caps, read under range locks held to the end of the statement.
        sql.Should().Contain("AND (SELECT ISNULL(SUM(w.EmailCodeSendCount), 0) FROM dbo.TwoFactorChallenges w WITH (UPDLOCK, HOLDLOCK) "
                             + "WHERE w.UserId = c.UserId AND w.EmailCodeSentAt > DATEADD(SECOND, -@WindowSeconds, @Now)) < @WindowMax");
        // The daily cap closes the statement: compared at its end, so nothing loosens it after.
        sql.Should().EndWith("AND (SELECT ISNULL(SUM(d.EmailCodeSendCount), 0) FROM dbo.TwoFactorChallenges d WITH (UPDLOCK, HOLDLOCK) "
                             + "WHERE d.UserId = c.UserId AND d.EmailCodeSentAt > DATEADD(HOUR, -24, @Now)) < @DailyMax");
        sql.Should().Contain("DATEADD(SECOND, -@WindowSeconds, @Now)) < @WindowMax AND (SELECT ISNULL(SUM(d.");

        var parameters = db.Commands[0].Parameters;
        parameters["PerChallengeMax"].Should().Be(TwoFactorChallenge.MaxEmailCodeSends);
        parameters["MaxAttempts"].Should().Be(TwoFactorChallenge.MaxAttempts);
        parameters["DailyMax"].Should().Be(10);
        parameters["Hard"].Should().Be(100);
        parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.SignIn);
    }

    [Fact]
    public async Task SendReservation_ReplacesTheLiveCode_KeepsTheEarlierOnes_AndNeverLowersACount()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new TwoFactorChallengeRepository(db).TryReserveEmailCodeSendAsync(Send(), CancellationToken.None);
        var sql = Sql(db.Commands[0]);

        sql.Should().StartWith("DECLARE @Now DATETIME2 = SYSUTCDATETIME(); UPDATE c SET EmailCodeSendCount = c.EmailCodeSendCount + 1, "
                               + "EmailCodeSentAt = @Now, ExpiresAt = DATEADD(MINUTE, @LifetimeMinutes, @Now), "
                               + "EmailCodeHash = CONCAT(c.EmailCodeHash + N';', @CodeHash) "
                               + "OUTPUT inserted.EmailCodeSendCount AS SendCount, inserted.ExpiresAt");
        // A resend writes no failure count and no attempt count — it only reads the
        // challenge's attempts, so an exhausted one gets no code: only a correct code,
        // or a completed password reset, settles the emailed code's count.
        sql.Should().NotContain("EmailFailedAttempts =").And.NotContain("AttemptCount =");
    }

    [Fact]
    public async Task SendReservation_ReturnsTheCountAndTheNewExpiry()
    {
        var expiresAt = new DateTime(2026, 10, 23, 9, 24, 0, DateTimeKind.Utc);
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new { SendCount = 2, ExpiresAt = expiresAt });

        var receipt = await new TwoFactorChallengeRepository(db).TryReserveEmailCodeSendAsync(Send(), CancellationToken.None);

        receipt.Should().Be(new EmailCodeSendReceipt(2, expiresAt));
    }

    // ── The emailed code's own counter ──

    private const string ExpectedEmailReservation =
        "UPDATE dbo.TwoFactorAuth SET EmailFailedAttempts = EmailFailedAttempts + 1, "
        + "EmailLockedUntil = CASE WHEN EmailFailedAttempts + 1 >= @MaxAttempts THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()) "
        + "ELSE EmailLockedUntil END, ModifiedAt = SYSUTCDATETIME() "
        + "OUTPUT inserted.EmailFailedAttempts "
        + "WHERE UserId = @UserId AND EmailCodeEnabledAt IS NOT NULL "
        + "AND (EmailLockedUntil IS NULL OR EmailLockedUntil <= SYSUTCDATETIME()) "
        + "AND (@Hard IS NULL OR EmailFailedAttempts < @Hard)";

    [Fact]
    public async Task EmailAttempt_IsCountedOnTheEmailColumnsAlone_UnderTheSameLimits_AndStampsTheFailure()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new TwoFactorStateStore(db).TryReserveEmailCodeAttemptAsync(Guid.NewGuid(), hardLimit: 100, CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(ExpectedEmailReservation);
        db.Commands[0].Parameters["MaxAttempts"].Should().Be(TwoFactorAuth.MaxFailedAttempts);
        db.Commands[0].Parameters["LockoutMinutes"].Should().Be(TwoFactorAuth.LockoutMinutes);
        db.Commands[0].Parameters["Hard"].Should().Be(100);
    }

    [Fact]
    public async Task ThePasswordReset_ClearsTheEmailCodesCountAndLock_AndNothingElse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorStateStore(db).ClearEmailCodeLockAsync(Guid.NewGuid(), CancellationToken.None);

        // No ModifiedAt stamp: the administrator's reset gate reads ModifiedAt as the
        // last counted failure, and a clearing is not one (review 99, F7).
        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(
            "UPDATE dbo.TwoFactorAuth SET EmailFailedAttempts = 0, EmailLockedUntil = NULL "
            + "WHERE UserId = @UserId AND (EmailFailedAttempts > 0 OR EmailLockedUntil IS NOT NULL)");
    }

    // ── The commit ──

    [Fact]
    public async Task EmailCommit_ConsumesItsChallenge_SpendsTheCodesAsRead_ThenSettlesTheEmailCount_InOneTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var challengeId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryCommitLoginAsync(
            challengeId, Guid.NewGuid(), SecondFactorProof.EmailCode(challengeId, CodesSeen), true, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        Sql(db.Commands[0]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET UsedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount - 1 "
            + "WHERE Id = @Id AND UsedAt IS NULL AND Purpose = @Purpose");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.SignIn);
        Sql(db.Commands[1]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET EmailCodeHash = NULL "
            + "WHERE Id = @ChallengeId AND UserId = @UserId AND EmailCodeHash = @CodesSeen");
        db.Commands[1].Parameters["CodesSeen"].Should().Be(CodesSeen);
        Sql(db.Commands[2]).Should().Be(
            "UPDATE dbo.TwoFactorAuth SET EmailFailedAttempts = 0, EmailLockedUntil = NULL, LastUsedAt = SYSUTCDATETIME(), "
            + "ModifiedAt = SYSUTCDATETIME() WHERE UserId = @UserId AND EmailCodeEnabledAt IS NOT NULL");
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task EmailCommit_WhenANewerCodeReplacedTheOneChecked_RollsBack_AndSaysTheChallengeWasLost()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            affectedFor: command => command.CommandText.Contains("EmailCodeHash] = @CodesSeen", StringComparison.Ordinal) ? 0 : 1);
        var challengeId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryCommitLoginAsync(
            challengeId, Guid.NewGuid(), SecondFactorProof.EmailCode(challengeId, CodesSeen), true, CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.ChallengeLost);
        db.Transactions[0].RolledBack.Should().BeTrue();
        db.Transactions[0].Committed.Should().BeFalse();
        db.Commands.Should().NotContain(command => Sql(command).StartsWith("UPDATE dbo.TwoFactorAuth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmailCommit_RefusesAProofEmailedForAnotherChallenge()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var commit = () => new TwoFactorStateStore(db).TryCommitLoginAsync(
            Guid.NewGuid(), Guid.NewGuid(), SecondFactorProof.EmailCode(Guid.NewGuid(), CodesSeen), true, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidOperationException>();
        db.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ARecoveryCommit_ConsumesOnlyAChallengeOfItsPurpose()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var challengeId = Guid.NewGuid();

        await new TwoFactorStateStore(db).TryCommitChallengeAsync(
            challengeId, TwoFactorChallengePurpose.DeletionRecovery, Guid.NewGuid(),
            SecondFactorProof.EmailCode(challengeId, CodesSeen), true, CancellationToken.None);

        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.DeletionRecovery);
    }

    // ── The purpose on every statement of the challenge table ──

    [Fact]
    public async Task TheTokenLookup_ReadsSignInChallengesOnly()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var keys = new FakeCredentialHasher();
        keys.Map("token", "token-hash");

        await new TwoFactorChallengeRepository(db).GetByTokenHashAsync(
            keys.Candidates(CredentialPurpose.TwoFactorChallenge, "token"), CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().EndWith(
            "FROM dbo.TwoFactorChallenges WHERE TokenHash IN (@Hashes1) AND Purpose = @Purpose");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.SignIn);
    }

    [Fact]
    public async Task ANewSignIn_InvalidatesSignInChallengesOnly()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorChallengeRepository(db).InvalidateAllForUserAsync(Guid.NewGuid(), CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET UsedAt = GETUTCDATE() WHERE UserId = @UserId AND UsedAt IS NULL AND Purpose = @Purpose");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.SignIn);
    }

    [Theory]
    [InlineData(TwoFactorChallengePurpose.SignIn)]
    [InlineData(TwoFactorChallengePurpose.DeletionRecovery)]
    public async Task TheAttemptReservation_NamesThePurpose(TwoFactorChallengePurpose purpose)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorChallengeRepository(db).TryReserveAttemptAsync(Guid.NewGuid(), purpose, 5, CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET AttemptCount = AttemptCount + 1 OUTPUT inserted.AttemptCount "
            + "WHERE Id = @Id AND UsedAt IS NULL AND ExpiresAt > SYSUTCDATETIME() AND AttemptCount < @MaxAttempts AND Purpose = @Purpose");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)purpose);
    }

    [Fact]
    public async Task TheSignInReservation_WithoutAPurposeArgument_IsTheSignInOne()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new TwoFactorChallengeRepository(db).TryReserveAttemptAsync(Guid.NewGuid(), 5, CancellationToken.None);

        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.SignIn);
    }

    [Fact]
    public async Task TheLiveRecoveryChallenge_IsReadByAccountAndPurpose()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new TwoFactorChallengeRepository(db).GetLiveForUserAsync(
            Guid.NewGuid(), TwoFactorChallengePurpose.DeletionRecovery, CancellationToken.None);

        // An exhausted challenge is not live: the recovery opens a new one instead of
        // spending the account's send budget on a code nobody can enter (review 99, F5).
        Sql(db.Commands.Should().ContainSingle().Subject).Should().EndWith(
            "FROM dbo.TwoFactorChallenges WHERE UserId = @UserId AND Purpose = @Purpose AND UsedAt IS NULL "
            + "AND ExpiresAt > SYSUTCDATETIME() AND AttemptCount < @MaxAttempts ORDER BY CreatedAt DESC");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.DeletionRecovery);
        db.Commands[0].Parameters["MaxAttempts"].Should().Be(TwoFactorChallenge.MaxAttempts);
    }

    [Fact]
    public async Task TheSweep_RemovesEveryPurposeByExpiry()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new TwoFactorChallengeRepository(db).CleanupExpiredAsync(DateTime.UtcNow, 500, CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(
            "DELETE TOP (@BatchSize) FROM dbo.TwoFactorChallenges WHERE ExpiresAt < @OlderThan",
            "each purpose's row expires by the same column, so one sweep keeps none of them past its retention");
    }

    [Fact]
    public async Task ANewChallenge_IsWrittenWithItsPurpose()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var challenge = TwoFactorChallenge.Create(
            Guid.NewGuid(), "hash", null, primaryMethod: AuthenticationMethods.Password,
            purpose: TwoFactorChallengePurpose.DeletionRecovery);

        await new TwoFactorChallengeRepository(db).CreateAsync(challenge, CancellationToken.None);

        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.DeletionRecovery);
    }

    // ── A recovery code on a row whose factor is the emailed code ──

    [Fact]
    public async Task ARecoveryCode_SettlesOnARowWhoseFactorIsTheEmailedCode_Too()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await new TwoFactorStateStore(db).TrySpendRecoveryCodeAsync(
            Guid.NewGuid(), SecondFactorProof.RecoveryCode("[\"h1\",\"h2\"]", "[\"h2\"]", qualified: false), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        Sql(db.Commands.Should().ContainSingle().Subject).Should().EndWith(
            "WHERE UserId = @UserId AND (IsEnabled = 1 OR EmailCodeEnabledAt IS NOT NULL) AND RecoveryCodes = @OldCodes");
    }

    // ── The dead letter of a sensitive type ──

    [Fact]
    public async Task ADeadLetter_RedactsASensitiveBody_InTheStatementThatKillsIt()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new NotificationOutboxRepository(db).MarkFailedAsync(
            Guid.NewGuid(), "Email sending disabled", DateTime.UtcNow, maxAttempts: 5, redactBody: true, CancellationToken.None);

        var sql = Sql(db.Commands.Should().ContainSingle().Subject);
        sql.Should().Contain("BodyHtml = CASE WHEN AttemptCount + 1 >= @MaxAttempts THEN @RedactedBody ELSE BodyHtml END");
        sql.Should().Contain("BodyText = CASE WHEN AttemptCount + 1 >= @MaxAttempts THEN @RedactedBody ELSE BodyText END");
        db.Commands[0].Parameters["RedactedBody"].Should().Be(Auth.Domain.Constants.NotificationTypeCodes.RedactedBody);
    }

    [Fact]
    public async Task AFailedAttempt_OfAnOrdinaryType_KeepsItsBody()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new NotificationOutboxRepository(db).MarkFailedAsync(
            Guid.NewGuid(), "SMTP down", DateTime.UtcNow, maxAttempts: 5, redactBody: false, CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().NotContain("BodyHtml").And.NotContain("BodyText");
    }

    // ── PR B: switching the emailed code on, off, and over to the app (OI-220) ──

    private static TwoFactorSnapshot EnabledApp(Guid userId) =>
        new(userId, "v2:app-secret", "[\"h1\"]", isEnabled: true, failedAttempts: 0, lockedUntil: null, recoveryCodesQualified: true);

    [Fact]
    public async Task Bind_OnAnAccountWithNoRow_SpendsTheEnrollmentCode_InsertsTheEmailedCode_AndSetsTheFlag_InOneTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryBindEmailCodeAsync(
            new EmailCodeBinding(userId, challengeId, CodesSeen, FactorSeen: null, "[\"new\"]"), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(4).And.OnlyContain(command => command.InTransaction);
        Sql(db.Commands[0]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET UsedAt = SYSUTCDATETIME(), AttemptCount = AttemptCount - 1 "
            + "WHERE Id = @Id AND UsedAt IS NULL AND Purpose = @Purpose");
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.EmailEnrollment,
            "only the enrollment's own challenge binds the emailed code");
        Sql(db.Commands[1]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET EmailCodeHash = NULL "
            + "WHERE Id = @ChallengeId AND UserId = @UserId AND EmailCodeHash = @CodesSeen");
        Sql(db.Commands[2]).Should().Be(
            "INSERT INTO dbo.TwoFactorAuth ( Id, UserId, SecretKey, RecoveryCodes, IsEnabled, FailedAttempts, CreatedAt, "
            + "EmailCodeEnabledAt, EmailFailedAttempts, RecoveryCodesQualified) VALUES ( @Id, @UserId, NULL, @RecoveryCodes, 0, 0, SYSUTCDATETIME(), "
            + "SYSUTCDATETIME(), 0, 0)",
            "no secret, not an enabled app, and recovery codes that do not count (FA3)");
        db.Commands[2].Parameters["RecoveryCodes"].Should().Be("[\"new\"]");
        Sql(db.Commands[3]).Should().StartWith("UPDATE dbo.Users SET IsTwoFactorEnabled = @IsTwoFactorEnabled");
        db.Commands[3].Parameters["IsTwoFactorEnabled"].Should().Be(true);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Bind_OverAnEnabledApp_ClearsItsSecretInTheSameStatement_KeepsItsCodes_OnlyWhileTheRowIsTheOneRead()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();

        await new TwoFactorStateStore(db).TryBindEmailCodeAsync(
            new EmailCodeBinding(userId, Guid.NewGuid(), CodesSeen, EnabledApp(userId), NewRecoveryCodesJson: null), CancellationToken.None);

        var bind = db.Commands[2];
        Sql(bind).Should().Be(
            "UPDATE dbo.TwoFactorAuth SET SecretKey = NULL, PendingSecretKey = NULL, PendingSecretCreatedAt = NULL, LastUsedTimeStep = NULL, "
            + "FailedAttempts = CASE WHEN @WasEnabled = 0 THEN 0 ELSE FailedAttempts END, "
            + "LockedUntil = CASE WHEN @WasEnabled = 0 THEN NULL ELSE LockedUntil END, "
            + "IsEnabled = 0, EnabledAt = NULL, EmailCodeEnabledAt = SYSUTCDATETIME(), EmailFailedAttempts = 0, EmailLockedUntil = NULL, "
            + "RecoveryCodes = CASE WHEN @NewCodes IS NULL THEN RecoveryCodes ELSE @NewCodes END, "
            + "RecoveryCodesQualified = CASE WHEN @NewCodes IS NULL THEN RecoveryCodesQualified ELSE 0 END, ModifiedAt = SYSUTCDATETIME() "
            + "WHERE UserId = @UserId AND EmailCodeEnabledAt IS NULL AND IsEnabled = @WasEnabled "
            + "AND (SecretKey = @SecretSeen OR (SecretKey IS NULL AND @SecretSeen IS NULL)) "
            + "AND (RecoveryCodes = @OldCodes OR (RecoveryCodes IS NULL AND @OldCodes IS NULL))",
            "the app goes and the emailed code comes in one statement (CK_TwoFactorAuth_SingleFactor), and only over the row read");
        bind.Parameters["WasEnabled"].Should().Be(true);
        bind.Parameters["SecretSeen"].Should().Be("v2:app-secret");
        bind.Parameters["OldCodes"].Should().Be("[\"h1\"]");
        bind.Parameters["NewCodes"].Should().BeNull("the app's codes stay, with their qualification");
    }

    [Fact]
    public async Task Bind_OverAPendingApp_TellsTheStatementSo_ItsCountAndLockGoWithTheApp()
    {
        // Review of #107: the pending app's failures, left on the shared count, would lock
        // the recovery codes issued with the emailed code from their first use.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();
        var pending = new TwoFactorSnapshot(userId, "v2:pending", null, isEnabled: false, failedAttempts: 5,
            lockedUntil: DateTime.UtcNow.AddMinutes(10));

        await new TwoFactorStateStore(db).TryBindEmailCodeAsync(
            new EmailCodeBinding(userId, Guid.NewGuid(), CodesSeen, pending, NewRecoveryCodesJson: "[\"new\"]"), CancellationToken.None);

        var bind = db.Commands[2];
        bind.Parameters["WasEnabled"].Should().Be(false, "the CASE resets the shared count only over a pending app");
        Sql(bind).Should().Contain("FailedAttempts = CASE WHEN @WasEnabled = 0 THEN 0 ELSE FailedAttempts END")
            .And.Contain("LockedUntil = CASE WHEN @WasEnabled = 0 THEN NULL ELSE LockedUntil END");
    }

    [Fact]
    public async Task Bind_WhenTheRowChangedSinceItWasRead_RollsBack_AndSaysTheFactorWasLost()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            affectedFor: command => command.CommandText.Contains("[EmailCodeEnabledAt] = SYSUTCDATETIME()", StringComparison.Ordinal) ? 0 : 1);
        var userId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryBindEmailCodeAsync(
            new EmailCodeBinding(userId, Guid.NewGuid(), CodesSeen, EnabledApp(userId), null), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.FactorLost);
        db.LastTransaction!.RolledBack.Should().BeTrue("the enrollment code is given back unspent");
        db.Commands.Should().NotContain(command => Sql(command).StartsWith("UPDATE dbo.Users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_WithAnEmailedCode_SpendsTheSwitchOffCode_ThenDeletesOnlyAnEmailedCodeRow_AndClearsTheFlag()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryRemoveEmailCodeAsync(
            userId, SecondFactorProof.EmailCode(challengeId, CodesSeen), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().HaveCount(5).And.OnlyContain(command => command.InTransaction);
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.EmailDisable,
            "only the switch-off's own challenge lets the emailed code go");
        db.Commands[0].Parameters["Id"].Should().Be(challengeId);
        Sql(db.Commands[1]).Should().Contain("SET EmailCodeHash = NULL");
        Sql(db.Commands[2]).Should().StartWith("UPDATE dbo.TwoFactorAuth SET EmailFailedAttempts = 0");
        Sql(db.Commands[3]).Should().Be("DELETE FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND EmailCodeEnabledAt IS NOT NULL");
        db.Commands[4].Parameters["IsTwoFactorEnabled"].Should().Be(false);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_WithARecoveryCode_SpendsItOnTheSetAsRead_ThenDeletes()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();

        await new TwoFactorStateStore(db).TryRemoveEmailCodeAsync(
            userId, SecondFactorProof.RecoveryCode("[\"h1\",\"h2\"]", "[\"h2\"]", qualified: false), CancellationToken.None);

        db.Commands.Should().HaveCount(3, "no challenge: a recovery code is spent on the row");
        Sql(db.Commands[0]).Should().EndWith("WHERE UserId = @UserId AND (IsEnabled = 1 OR EmailCodeEnabledAt IS NOT NULL) AND RecoveryCodes = @OldCodes");
        Sql(db.Commands[1]).Should().Be("DELETE FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND EmailCodeEnabledAt IS NOT NULL");
    }

    [Fact]
    public async Task Remove_WhenTheRecoveryCodeWasSpentFirst_RollsBack_AndSaysTheCodesChanged()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            affectedFor: command => command.CommandText.Contains("[RecoveryCodes] = @OldCodes", StringComparison.Ordinal) ? 0 : 1,
            rowFor: command => command.CommandText.Contains("SELECT CAST(CASE WHEN [EmailCodeEnabledAt]", StringComparison.Ordinal)
                ? new { HasEmailCode = true }
                : null);

        var outcome = await new TwoFactorStateStore(db).TryRemoveEmailCodeAsync(
            Guid.NewGuid(), SecondFactorProof.RecoveryCode("[\"h1\"]", "[]", qualified: false), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.RecoveryCodesChanged);
        db.Transactions[0].RolledBack.Should().BeTrue();
        db.Commands.Should().NotContain(command => Sql(command).StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplaceWithTheApp_SpendsTheProof_ThenSwitchesTheAppOnAndTheEmailedCodeOff_InOneStatement_InOneTransaction()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("OUTPUT deleted", StringComparison.Ordinal)
                ? new { LastUsedTimeStep = (long?)null }
                : null);
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        var outcome = await new TwoFactorStateStore(db).TryReplaceEmailCodeWithTotpAsync(
            userId, SecondFactorProof.EmailCode(challengeId, CodesSeen), "v2:new-app-secret", "[\"qualified\"]", 59_313_872, true,
            bindCodeId: null, new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.Committed);
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Commands[0].Parameters["Purpose"].Should().Be((byte)TwoFactorChallengePurpose.EmailDisable);
        var swap = db.Commands.Single(command => Sql(command).StartsWith("UPDATE dbo.TwoFactorAuth SET IsEnabled = 1", StringComparison.Ordinal));
        Sql(swap).Should().Contain("EmailCodeEnabledAt = NULL, EmailFailedAttempts = 0, EmailLockedUntil = NULL")
            .And.Contain("RecoveryCodesQualified = 1")
            .And.Contain("WHERE UserId = @UserId AND IsEnabled = 0 AND EmailCodeEnabledAt IS NOT NULL AND SecretKey = @SecretSeen");
        db.Commands.ToList().IndexOf(swap).Should().BeGreaterThan(2, "the proof is spent before the row changes");
        db.Commands.Should().Contain(command => Sql(command).StartsWith("UPDATE dbo.Users", StringComparison.Ordinal));
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceWithTheApp_WhenTheRowIsNoLongerTheEmailedCodes_RollsBack_AndSaysTheFactorWasLost()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT CAST(CASE WHEN [EmailCodeEnabledAt]", StringComparison.Ordinal)
                ? new { HasEmailCode = false }
                : null);

        var outcome = await new TwoFactorStateStore(db).TryReplaceEmailCodeWithTotpAsync(
            Guid.NewGuid(), SecondFactorProof.RecoveryCode("[\"h1\"]", "[]", qualified: false), "v2:s", "[]", 59_313_872, true,
            bindCodeId: null, new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None);

        outcome.Should().Be(LoginCommitOutcome.FactorLost);
        db.Transactions[0].RolledBack.Should().BeTrue();
        db.Transactions[0].Committed.Should().BeFalse();
    }

    [Fact]
    public async Task ThePasswordReset_EndsEveryPurpose_AndRestoresTheSendBudgetOnEndedChallengesOnly()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();
        var repository = new TwoFactorChallengeRepository(db);

        await repository.InvalidateEveryPurposeForUserAsync(userId, CancellationToken.None);
        await repository.RestoreEmailCodeSendBudgetAsync(userId, CancellationToken.None);

        Sql(db.Commands[0]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET UsedAt = SYSUTCDATETIME() WHERE UserId = @UserId AND UsedAt IS NULL",
            "every purpose ends — no Purpose condition (OI-220 (3))");
        Sql(db.Commands[1]).Should().Be(
            "UPDATE dbo.TwoFactorChallenges SET EmailCodeSentAt = NULL WHERE UserId = @UserId AND UsedAt IS NOT NULL AND EmailCodeSentAt IS NOT NULL",
            "the caps sum codes by EmailCodeSentAt; a live challenge keeps its time and its own three sends (OI-220 (5))");
        db.Commands.Should().OnlyContain(command => (Guid)command.Parameters["UserId"]! == userId);
    }

    [Fact]
    public void TheSendBudget_IsRestoredByThePasswordResetAlone()
    {
        // EO6: a leaked password still spends the budget for a day. Only the reset of
        // the password by its emailed link — the holder of the mailbox — restores it;
        // whoever else could call the restore is named here, and nobody else may be.
        var callers = ApiSourceScan.ProductionSources()
            .Where(file => file.Source.Contains(".RestoreEmailCodeSendBudgetAsync(", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.File))
            .ToList();

        callers.Should().Equal(["ResetPasswordCommandHandler.cs"]);
    }
}
