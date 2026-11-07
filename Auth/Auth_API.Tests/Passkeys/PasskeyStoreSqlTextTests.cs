using System.Data;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK6, PK12, PK17): the passkey and challenge statements, as Dapper sends them. Each
/// guarantee lives in one SQL condition, so each is pinned on the statement's text: a challenge
/// is spent once, by its own account, before the response is checked; the limit is read under
/// a lock held by the insert; a user handle is set once; a passkey is revoked or renamed only
/// by its owner; and the 72-hour cut keeps the older ones.
/// </summary>
/// <remarks>
/// <para>WebAuthnChallengeStoreSqlTextTests' rows (the card's name) are the challenge tests here.</para>
/// <para>
/// Deliberate breaks, each verified red: <c>[UsedAt] IS NULL</c> dropped from the consumption;
/// <c>[Purpose] = @Purpose</c> dropped; <c>WITH (UPDLOCK, HOLDLOCK)</c> dropped; <c>AND
/// [UserId] = @UserId</c> dropped from the revocation; <c>[WebAuthnUserHandle] IS NULL</c>
/// dropped; the 72-hour cut written <c>&gt;=</c>. REVIEW 150: the session's EXISTS dropped
/// from the insert (F1); the floor, or its lock hints, dropped (F4); the index dropped (F5);
/// <c>[UserId] = @UserId</c> dropped from the list, and its order written <c>DESC</c> (F6, F8).
/// S20 PR B's dev run: the challenge's expiry and creation time sent with Dapper's default type.
/// </para>
/// </remarks>
public class PasskeyStoreSqlTextTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    // ── The challenge store (WebAuthnChallengeStoreSqlTextTests) ──

    [Fact]
    public async Task ConsumeRegistration_IsOneStatement_ThatSpendsOnlyAnUnusedUnexpiredRegistrationChallengeOfTheCaller()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var candidates = FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "fp-current", "fp-previous");

        await Challenges(db).TryConsumeRegistrationAsync(candidates, UserId, CancellationToken.None);

        var command = db.Commands.Should().ContainSingle().Subject;
        Squash(WebAuthnChallengeRepository.ConsumeRegistrationSql).Should().Be(
            "UPDATE [dbo].[WebAuthnChallenges] SET [UsedAt] = SYSUTCDATETIME() OUTPUT inserted.[OptionsJson] "
            + "WHERE [HandleHash] IN @Hashes AND [Purpose] = @Purpose AND [UserId] = @UserId "
            + "AND [UsedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
            "spent in the statement that finds it, before the response is checked (PK6)");
        command.CommandText.Should().Contain("[HandleHash] IN (", "Dapper expands the candidates into the statement sent");
        command.Parameters["Purpose"].Should().Be((byte)WebAuthnChallengePurpose.Registration);
        command.Parameters["UserId"].Should().Be(UserId);
        command.Parameters.Values.Should().Contain("fp-current").And.Contain("fp-previous", "every accepted fingerprint (KR9)");
    }

    [Fact]
    public async Task Create_StoresTheFingerprint_ThePurpose_AndTheFiveMinuteExpiry()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var issuedAt = new DateTime(2026, 11, 5, 9, 14, 0, DateTimeKind.Utc);
        var challenge = WebAuthnChallenge.IssueForRegistration(UserId, "fingerprint", "{\"challenge\":\"x\"}", issuedAt);

        await Challenges(db).CreateAsync(challenge, CancellationToken.None);

        var command = db.Commands.Single();
        command.CommandText.Should().Be(WebAuthnChallengeRepository.InsertSql);
        command.Parameters["HandleHash"].Should().Be("fingerprint");
        command.Parameters["Purpose"].Should().Be((byte)WebAuthnChallengePurpose.Registration);
        command.Parameters["ExpiresAt"].Should().Be(issuedAt.AddMinutes(5));
    }

    [Fact]
    public async Task Create_WritesTheTimesAsDateTime2_SoAPageThatAsksAgainIsGivenTheSameExpiresAt()
    {
        // Found by S20 PR B's dev run: with Dapper's default DATETIME the stored expiry was
        // rounded to a 300th of a second, so the options asked again (T-B2) answered an
        // expiresAt up to 3 ms off the first answer.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var issuedAt = new DateTime(2026, 11, 7, 3, 10, 33, DateTimeKind.Utc).AddTicks(239_882);
        var challenge = WebAuthnChallenge.IssueForAssertion("fingerprint", "{\"challenge\":\"x\"}", issuedAt);

        await Challenges(db).CreateAsync(challenge, CancellationToken.None);

        var command = db.Commands.Single();
        command.Parameters["ExpiresAt"].Should().Be(challenge.ExpiresAt);
        command.ParameterTypes["ExpiresAt"].Should().Be(DbType.DateTime2,
            "Dapper's default datetime rounds to 3 ms, and the options asked again would carry another expiresAt");
        command.ParameterTypes["CreatedAt"].Should().Be(DbType.DateTime2);
    }

    [Fact]
    public async Task TheSweep_DeletesExpiredChallenges_SpentOrNot_InBatches()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 3);
        var cutoff = DateTime.UtcNow.AddDays(-1);

        (await Challenges(db).CleanupExpiredAsync(cutoff, 500, CancellationToken.None)).Should().Be(3);

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().Be("DELETE TOP (@BatchSize) FROM [dbo].[WebAuthnChallenges] WHERE [ExpiresAt] < @OlderThan");
        command.Parameters["OlderThan"].Should().Be(cutoff);
        command.Parameters["BatchSize"].Should().Be(500);
    }

    [Fact]
    public async Task LiveRegistration_IsAnUnusedUnexpiredRegistrationChallengeOfTheCaller()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        (await Challenges(db).HasLiveRegistrationAsync(UserId, CancellationToken.None)).Should().BeTrue();

        Squash(db.Commands.Single().CommandText).Should().Contain(
            "WHERE [UserId] = @UserId AND [Purpose] = @Purpose AND [UsedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME())");
    }

    [Fact]
    public async Task WithoutTheSchema_NoChallengeStatementIsSent()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var challenges = new WebAuthnChallengeRepository(db, PasskeyTestDoubles.Schema(present: false));

        (await challenges.TryConsumeRegistrationAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "x"), UserId, CancellationToken.None))
            .Should().BeNull();
        (await challenges.HasLiveRegistrationAsync(UserId, CancellationToken.None)).Should().BeFalse();
        (await challenges.CleanupExpiredAsync(DateTime.UtcNow, 10, CancellationToken.None)).Should().Be(0);
        db.Commands.Should().BeEmpty("a statement naming a missing table fails when it is bound");
    }

    // ── The passkey store ──

    [Fact]
    public async Task TryCreate_ReadsTheLimitUnderARangeLock_InTheInsertItself()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var passkey = PasskeyTestDoubles.Live(UserId, DateTime.UtcNow);

        (await Passkeys(db).TryCreateAsync(passkey, UserPasskey.MaxLivePerAccount, bindCodeId: null, SessionId, requireAnotherFactor: false, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.Inserted);

        var insert = db.Commands.Single();
        insert.InTransaction.Should().BeTrue();
        Squash(insert.CommandText).Should().Contain(
            "WHERE (SELECT COUNT(*) FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL) < @MaxLive",
            "of two registrations that would each be the eleventh, one is refused");
        insert.Parameters["MaxLive"].Should().Be(10);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    /// <summary>
    /// REVIEW 150 F1: the insert names the session the registration's checks accepted, read
    /// under a lock its end must wait for. A password change, a reset or a lock ends the
    /// sessions before it revokes the recent passkeys, so a registration in flight either
    /// commits first, and is revoked, or finds its session ended.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the EXISTS on the session dropped from the insert.</remarks>
    [Fact]
    public async Task TryCreate_InsertsOnlyWhileTheSessionThatAuthorisedItIsLive_UnderALockItsEndWaitsFor()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, null, SessionId, requireAnotherFactor: false, CancellationToken.None);

        var insert = db.Commands.Single();
        Squash(insert.CommandText).Should().Contain(
            "AND EXISTS (SELECT 1 FROM [dbo].[UserSessions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @SessionId AND [UserId] = @UserId AND [EndedAt] IS NULL)",
            "a registration whose session a recovery ended stores nothing");
        insert.Parameters["SessionId"].Should().Be(SessionId);
        insert.Parameters["UserId"].Should().Be(UserId);
    }

    /// <summary>
    /// REVIEW-FIXES 150 F19 (D-150-5 (a)): with email off, the row is written only while the
    /// account still holds another factor — the authenticator app or another live passkey — read
    /// under a lock the app's switch-off waits for. The switch-off deletes the app's row first and
    /// then reads the passkeys under a range lock, so the two at the same moment never leave a
    /// passkey as an account's only factor.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the condition dropped from the insert.</remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryCreate_WithEmailOff_WritesOnlyWhileAnotherFactorRemains_UnderALockTheSwitchOffWaitsFor(bool emailOff)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, null, SessionId, requireAnotherFactor: emailOff, CancellationToken.None);

        var insert = db.Commands.Single();
        Squash(insert.CommandText).Should().EndWith(
            "AND (@RequireAnotherFactor = 0 "
            + "OR EXISTS (SELECT 1 FROM [dbo].[TwoFactorAuth] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = @UserId AND [IsEnabled] = 1) "
            + "OR EXISTS (SELECT 1 FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL AND [IsUserVerified] = 1))",
            "with email off, the app's row or another passkey must still be there when the row is written");
        insert.Parameters["RequireAnotherFactor"].Should().Be(emailOff);
    }

    /// <summary>
    /// REVIEW-FIXES 150 F19: an insert that lost because the app went meanwhile, with email off,
    /// says so — read in its transaction before the rollback — and stores nothing.
    /// </summary>
    [Fact]
    public async Task TryCreate_WithEmailOff_TheOtherFactorGone_RollsBack_AndSaysSo()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            scalarFor: command => command.CommandText == UserPasskeyRepository.SessionLiveSql);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, null, SessionId, requireAnotherFactor: true, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.AnotherFactorRequired);

        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        db.Commands[2].CommandText.Should().Be(UserPasskeyRepository.AnotherFactorSql, "which condition lost, read before the rollback");
        db.Commands[2].Parameters["RpId"].Should().Be(PasskeyTestDoubles.RpId);
        db.LastTransaction!.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task TryCreate_AtTheLimit_RollsBack_AndSaysSo()
    {
        // The insert loses; the session is still live, so it was the limit.
        var db = new RecordingDbConnectionFactory(affectedRows: 0, scalarFor: _ => true);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, null, SessionId, requireAnotherFactor: false, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.LimitReached);
        db.LastTransaction!.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task TryCreate_AfterTheSessionEnded_RollsBack_AndSaysSo_InTheSameTransaction()
    {
        var codeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            affectedFor: command => command.CommandText == SingleUseCodeStatements.TwoFactorBindCodes.Consume ? 1 : 0,
            scalarFor: _ => false);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, codeId, SessionId, requireAnotherFactor: false, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.SessionEnded);

        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        db.Commands[2].CommandText.Should().Be(UserPasskeyRepository.SessionLiveSql, "which condition lost, read before the rollback");
        Squash(UserPasskeyRepository.SessionLiveSql).Should().Contain(
            "WHERE [Id] = @SessionId AND [UserId] = @UserId AND [EndedAt] IS NULL");
        db.LastTransaction!.RolledBack.Should().BeTrue("the emailed code stays unspent");
    }

    [Fact]
    public async Task TryCreate_WithTheEmailedCode_SpendsItFirst_InTheSameTransaction()
    {
        var codeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, codeId, SessionId, requireAnotherFactor: false, CancellationToken.None);

        db.Commands.Should().HaveCount(2).And.OnlyContain(command => command.InTransaction);
        db.Commands[0].CommandText.Should().Be(SingleUseCodeStatements.TwoFactorBindCodes.Consume);
        db.Commands[0].Parameters["Id"].Should().Be(codeId);
        db.Commands[1].CommandText.Should().Be(UserPasskeyRepository.InsertWithinLimitSql);
    }

    [Fact]
    public async Task TryCreate_TheEmailedCodeSpentElsewhere_InsertsNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, Guid.NewGuid(), SessionId, requireAnotherFactor: false, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.BindCodeLost);
        db.Commands.Should().ContainSingle("the insert never runs");
        db.LastTransaction!.RolledBack.Should().BeTrue();
    }

    // ── The reads a person or a policy makes (REVIEW 150 F6) ──

    /// <summary>
    /// The caller's own live passkeys of the RP ID in force, oldest first. Each of the three
    /// conditions is what keeps another account's rows, a revoked row or a row of another RP
    /// ID out of the list, the exclusions and the password notice.
    /// </summary>
    /// <remarks>Deliberate breaks, each verified red: <c>[UserId] = @UserId</c> dropped; the order written <c>DESC</c>.</remarks>
    [Fact]
    public async Task GetLive_ReadsOnlyTheCallersLiveRowsOfTheRpId_OldestFirst()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        await Passkeys(db).GetLiveAsync(UserId, PasskeyTestDoubles.RpId, CancellationToken.None);

        var command = db.Commands.Single();
        command.CommandText.Should().Be(UserPasskeyRepository.LiveSql);
        Squash(command.CommandText).Should().EndWith(
            "FROM [dbo].[UserPasskeys] WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL ORDER BY [CreatedAt], [Id]",
            "oldest first, as the guides say and the console shows");
        command.Parameters["UserId"].Should().Be(UserId);
        command.Parameters["RpId"].Should().Be(PasskeyTestDoubles.RpId);
    }

    [Fact]
    public async Task GetLiveById_ReadsOnlyTheCallersLiveRowOfTheRpId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var passkeyId = Guid.NewGuid();

        (await Passkeys(db).GetLiveByIdAsync(UserId, passkeyId, PasskeyTestDoubles.RpId, CancellationToken.None)).Should().BeNull();

        var command = db.Commands.Single();
        command.CommandText.Should().Be(UserPasskeyRepository.LiveByIdSql);
        Squash(command.CommandText).Should().EndWith(
            "FROM [dbo].[UserPasskeys] WHERE [Id] = @Id AND [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL",
            "another account's passkey id finds nothing");
        command.Parameters["Id"].Should().Be(passkeyId);
        command.Parameters["UserId"].Should().Be(UserId);
    }

    [Fact]
    public async Task CountLive_CountsOnlyTheCallersLiveRowsOfTheRpId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 2);

        (await Passkeys(db).CountLiveAsync(UserId, PasskeyTestDoubles.RpId, CancellationToken.None)).Should().Be(2);

        var command = db.Commands.Single();
        command.CommandText.Should().Be(UserPasskeyRepository.CountLiveSql);
        Squash(command.CommandText).Should().Be(
            "SELECT COUNT(*) FROM [dbo].[UserPasskeys] WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL");
        command.Parameters["UserId"].Should().Be(UserId);
    }

    /// <summary>
    /// REVIEW 150 F4: a bound administrator's removal keeps the floor in the statement itself,
    /// read under a range lock held to its end, so two removals at once cannot take the last two.
    /// </summary>
    /// <remarks>Deliberate breaks, each verified red: the floor dropped; its lock hints dropped.</remarks>
    [Fact]
    public async Task RevokeOneUnlessLast_KeepsTheLastLivePasskey_UnderARangeLock()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var passkeyId = Guid.NewGuid();

        (await Passkeys(db).TryRevokeUnlessLastAsync(UserId, passkeyId, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, UserId, CancellationToken.None))
            .Should().BeNull();

        var command = db.Commands.Single();
        command.CommandText.Should().Be(UserPasskeyRepository.RevokeOneUnlessLastSql);
        Squash(command.CommandText).Should().EndWith(
            "WHERE [Id] = @Id AND [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL "
            + "AND (SELECT COUNT(*) FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL) > 1",
            "of two removals that would each take one of the last two, one changes nothing");
        command.Parameters["Id"].Should().Be(passkeyId);
        command.Parameters["UserId"].Should().Be(UserId);
    }

    [Fact]
    public void TheLiveRpIdIndex_LetsPk3sCheckSeek_InsteadOfReadingEveryLiveRow()
    {
        // REVIEW 150 F5: "[RpId] <> @RpId" on every passkey request, before authentication.
        var table = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "UserPasskeys.sql"));

        Squash(table).Should().Contain(
            "CREATE NONCLUSTERED INDEX [IX_UserPasskeys_LiveRpId] ON [dbo].[UserPasskeys] ([RpId]) WHERE [RevokedAt] IS NULL;");
    }

    [Fact]
    public async Task EnsureUserHandle_SetsItOnlyWhileTheColumnIsEmpty_AndReadsTheValueThatWon()
    {
        Squash(UserPasskeyRepository.EnsureUserHandleSql).Should().Be(
            "UPDATE [dbo].[Users] SET [WebAuthnUserHandle] = @Candidate WHERE [Id] = @UserId AND [WebAuthnUserHandle] IS NULL; "
            + "SELECT [WebAuthnUserHandle] FROM [dbo].[Users] WHERE [Id] = @UserId;");

        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new { WebAuthnUserHandle = new byte[] { 7, 7 } });
        (await Passkeys(db).EnsureUserHandleAsync(UserId, [1, 2], CancellationToken.None)).Should().Equal(7, 7);
    }

    [Fact]
    public async Task RevokeOne_IsKeyedOnTheOwner_TheRpId_AndALiveRow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var passkeyId = Guid.NewGuid();

        (await Passkeys(db).TryRevokeAsync(UserId, passkeyId, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, UserId, CancellationToken.None))
            .Should().BeNull();

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().EndWith(
            "WHERE [Id] = @Id AND [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL",
            "another account's passkey id finds nothing");
        command.Parameters["UserId"].Should().Be(UserId);
        command.Parameters["Id"].Should().Be(passkeyId);
        command.Parameters["Reason"].Should().Be(PasskeyRemovalReasons.User);
    }

    [Fact]
    public async Task Rename_IsKeyedOnTheOwner_TheRpId_AndALiveRow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        (await Passkeys(db).TryRenameAsync(UserId, Guid.NewGuid(), PasskeyTestDoubles.RpId, "Phone", CancellationToken.None)).Should().BeTrue();

        Squash(db.Commands.Single().CommandText).Should().Be(
            "UPDATE [dbo].[UserPasskeys] SET [Name] = @Name WHERE [Id] = @Id AND [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL");
    }

    [Fact]
    public async Task TheSeventyTwoHourCut_RevokesOnlyLiveRowsRegisteredAfterIt_OfAnyRpId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var cutoff = new DateTime(2026, 11, 2, 9, 14, 0, DateTimeKind.Utc);

        await Passkeys(db).RevokeRegisteredAfterAsync(UserId, cutoff, PasskeyRemovalReasons.PasswordReset, UserId, CancellationToken.None);

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().EndWith(
            "WHERE [UserId] = @UserId AND [RevokedAt] IS NULL AND [CreatedAt] > @RegisteredAfter",
            "one of 73 hours stays; one of 71 hours goes");
        command.Parameters["RegisteredAfter"].Should().Be(cutoff);
        command.Parameters["Reason"].Should().Be(PasskeyRemovalReasons.PasswordReset);
    }

    [Fact]
    public async Task OtherRpIdLive_AsksForAnyLiveRowOfAnotherRpId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        (await Passkeys(db).HasLiveOfAnotherRpIdAsync(PasskeyTestDoubles.RpId, CancellationToken.None)).Should().BeTrue();

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().Contain("WHERE [RevokedAt] IS NULL AND [RpId] <> @RpId)");
        command.Parameters["RpId"].Should().Be(PasskeyTestDoubles.RpId);
    }

    [Fact]
    public async Task TheAdministratorsReset_RevokesEveryLivePasskey_InItsTransaction()
    {
        // M8(4): with the factor row and the flag, so the caller raises one event per passkey.
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);
        var store = new TwoFactorStateStore(db, NullLogger<TwoFactorStateStore>.Instance, new PasskeyRpIdAtStartup(PasskeyTestDoubles.RpId), PasskeyTestDoubles.Schema());
        var resetBy = Guid.NewGuid();

        (await store.TryResetAsync(UserId, resetBy, CancellationToken.None)).Should().NotBeNull();

        var revoke = db.Commands.Single(command => command.CommandText.Contains("UPDATE [dbo].[UserPasskeys]", StringComparison.Ordinal));
        revoke.InTransaction.Should().BeTrue();
        Squash(revoke.CommandText).Should().EndWith("WHERE [UserId] = @UserId AND [RevokedAt] IS NULL");
        revoke.Parameters["Reason"].Should().Be(PasskeyRemovalReasons.PeerReset);
        revoke.Parameters["ResetBy"].Should().Be(resetBy);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task TheAdministratorsReset_WithoutTheSchema_SendsNoPasskeyStatement()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var store = new TwoFactorStateStore(db, NullLogger<TwoFactorStateStore>.Instance, new PasskeyRpIdAtStartup(PasskeyTestDoubles.RpId), PasskeyTestDoubles.Schema(present: false));

        (await store.TryResetAsync(UserId, Guid.NewGuid(), CancellationToken.None))!.RevokedPasskeys.Should().BeEmpty();
        db.Commands.Should().NotContain(command => command.CommandText.Contains("UserPasskeys"));
    }

    // ── S20 PR B: the sign-in ──

    /// <remarks>Deliberate break, verified red: <c>[UsedAt] IS NULL</c> dropped from the consume.</remarks>
    [Fact]
    public async Task ConsumeAssertion_IsOneStatement_ThatSpendsOnlyAnUnusedUnexpiredSignInChallengeOfNoAccount()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        var candidates = FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "fp-current", "fp-previous");

        (await Challenges(db).TryConsumeAssertionAsync(candidates, CancellationToken.None)).Should().BeNull();

        var command = db.Commands.Should().ContainSingle().Subject;
        Squash(WebAuthnChallengeRepository.ConsumeAssertionSql).Should().Be(
            "UPDATE [dbo].[WebAuthnChallenges] SET [UsedAt] = SYSUTCDATETIME() OUTPUT inserted.[OptionsJson] "
            + "WHERE [HandleHash] IN @Hashes AND [Purpose] = @Purpose AND [UserId] IS NULL "
            + "AND [UsedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
            "spent in the statement that finds it, before anything is checked (PK6)");
        command.CommandText.Should().Contain("[HandleHash] IN (");
        command.Parameters["Purpose"].Should().Be((byte)WebAuthnChallengePurpose.Assertion,
            "a registration challenge never answers a sign-in");
        command.Parameters.Values.Should().Contain("fp-current").And.Contain("fp-previous");
    }

    /// <remarks>Deliberate breaks, each verified red: the re-issue read on <c>CreatedAt</c>; its <c>[UsedAt] IS NULL</c> dropped.</remarks>
    [Fact]
    public async Task ReusableAssertion_ReadsOnlyALiveUnspentSignInChallenge_WithMoreThanAMinuteLeft_ByTheDatabasesClock()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        (await Challenges(db).GetReusableAssertionAsync(
            FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "fp"), CancellationToken.None)).Should().BeNull();

        db.Commands.Should().ContainSingle().Which.CommandText.Should().Contain("[HandleHash] IN (");
        var sql = Squash(WebAuthnChallengeRepository.ReusableAssertionSql);
        sql.Should().StartWith("SELECT").And.NotContain("UPDATE", "the read spends nothing");
        sql.Should().EndWith(
            "WHERE [HandleHash] IN @Hashes AND [Purpose] = @Purpose AND [UserId] IS NULL AND [UsedAt] IS NULL "
            + "AND [ExpiresAt] > DATEADD(SECOND, 60, SYSUTCDATETIME())");
        Squash(WebAuthnChallengeRepository.ReusableAssertionSql).Should().NotContain("[CreatedAt] >", "by what is left, never by when it was issued");
        db.LastCommand!.Parameters["Purpose"].Should().Be((byte)WebAuthnChallengePurpose.Assertion);
    }

    [Fact]
    public async Task ReusableAssertion_ReadsBackTheChallengeItFinds()
    {
        var id = Guid.NewGuid();
        var expires = DateTime.UtcNow.AddMinutes(4);
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new
        {
            Id = id, HandleHash = "fp", Purpose = (byte)2, UserId = (Guid?)null, OptionsJson = "{}",
            ExpiresAt = expires, UsedAt = (DateTime?)null, CreatedAt = expires.AddMinutes(-5),
        });

        var challenge = await Challenges(db).GetReusableAssertionAsync(
            FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "fp"), CancellationToken.None);

        challenge!.Id.Should().Be(id);
        challenge.Purpose.Should().Be(WebAuthnChallengePurpose.Assertion);
        challenge.ExpiresAt.Should().Be(expires);
        challenge.OptionsJson.Should().Be("{}");
    }

    /// <summary>P7-R15: the credential id goes as bytes, through the unique index.</summary>
    [Fact]
    public async Task LiveByCredentialId_ComparesTheBytes_OfALiveRowOfTheRpId_OfAnyAccount()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);
        byte[] credentialId = [0x41, 0x62, 0x43];

        (await Passkeys(db).GetLiveByCredentialIdAsync(credentialId, PasskeyTestDoubles.RpId, CancellationToken.None)).Should().BeNull();

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().EndWith(
            "FROM [dbo].[UserPasskeys] WHERE [CredentialId] = @CredentialId AND [RpId] = @RpId AND [RevokedAt] IS NULL");
        Squash(command.CommandText).Should().NotContain("[UserId] =", "the response's user handle says whose it must be");
        command.Parameters["CredentialId"].Should().BeOfType<byte[]>().Which.Should().Equal(credentialId);
    }

    [Fact]
    public async Task UserHandle_IsReadWhateverTheAccountsState_AndNothingElseOfIt()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        await Passkeys(db).GetUserHandleAsync(UserId, CancellationToken.None);

        Squash(db.Commands.Single().CommandText).Should().Be(
            "SELECT [WebAuthnUserHandle] FROM [dbo].[Users] WHERE [Id] = @UserId",
            "an account waiting for deletion is read too (FA6), and nothing but its handle");
    }

    /// <remarks>Deliberate break, verified red: <c>[RevokedAt] IS NULL</c> dropped from the record.</remarks>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task RecordAssertion_IsOneStatement_OnlyWhileTheRowIsLive_AndItsCounterIsTheOneChecked(int rows, bool recorded)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: rows);
        var passkey = PasskeyTestDoubles.Live(UserId, DateTime.UtcNow.AddDays(-1));
        passkey.RecordAssertion(signCount: 7, isBackupEligible: true, isBackedUp: true, DateTime.UtcNow).Should().Be(PasskeyCounterCheck.Advanced);

        (await Passkeys(db).TryRecordAssertionAsync(passkey, storedSignCount: 0, CancellationToken.None)).Should().Be(recorded);

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().Be(
            "UPDATE [dbo].[UserPasskeys] SET [SignCount] = @SignCount, [LastUsedAt] = @LastUsedAt, "
            + "[IsBackupEligible] = @IsBackupEligible, [IsBackedUp] = @IsBackedUp "
            + "WHERE [Id] = @Id AND [RevokedAt] IS NULL AND [SignCount] = @StoredSignCount",
            "a removal, a reset or another sign-in that got there first matches nothing (T-B4)");
        command.Parameters["SignCount"].Should().Be(7L);
        command.Parameters["StoredSignCount"].Should().Be(0L);
        command.Parameters["IsBackupEligible"].Should().Be(true);
        command.Parameters["IsBackedUp"].Should().Be(true);
    }

    [Fact]
    public async Task SealCounterRegression_WritesTheSeal_AndNeverRemovesOrRevokesThePasskey()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var at = DateTime.UtcNow;
        var passkeyId = Guid.NewGuid();

        await Passkeys(db).SealCounterRegressionAsync(passkeyId, at, CancellationToken.None);

        var command = db.Commands.Single();
        Squash(command.CommandText).Should().Be(
            "UPDATE [dbo].[UserPasskeys] SET [LastCounterRegressionAt] = @At WHERE [Id] = @Id");
        command.Parameters["At"].Should().Be(at);
        command.Parameters["Id"].Should().Be(passkeyId);
    }

    [Fact]
    public async Task WithoutTheSchema_NoSignInStatementIsSent()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var challenges = new WebAuthnChallengeRepository(db, PasskeyTestDoubles.Schema(present: false));
        var passkeys = new UserPasskeyRepository(db, PasskeyTestDoubles.Schema(present: false));
        var candidates = FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebAuthnChallenge, "fp");

        (await challenges.TryConsumeAssertionAsync(candidates, CancellationToken.None)).Should().BeNull();
        (await challenges.GetReusableAssertionAsync(candidates, CancellationToken.None)).Should().BeNull();
        (await passkeys.GetLiveByCredentialIdAsync([1], PasskeyTestDoubles.RpId, CancellationToken.None)).Should().BeNull();
        (await passkeys.GetUserHandleAsync(UserId, CancellationToken.None)).Should().BeNull();
        (await passkeys.TryRecordAssertionAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 0, CancellationToken.None)).Should().BeFalse();
        await passkeys.SealCounterRegressionAsync(Guid.NewGuid(), DateTime.UtcNow, CancellationToken.None);

        db.Commands.Should().BeEmpty();
    }

    private static WebAuthnChallengeRepository Challenges(RecordingDbConnectionFactory db) => new(db, PasskeyTestDoubles.Schema());

    private static UserPasskeyRepository Passkeys(RecordingDbConnectionFactory db) => new(db, PasskeyTestDoubles.Schema());

    private static string Squash(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
}
