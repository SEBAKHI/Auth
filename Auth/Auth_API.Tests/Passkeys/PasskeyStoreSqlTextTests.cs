using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
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
/// dropped; the 72-hour cut written <c>&gt;=</c>.
/// </para>
/// </remarks>
public class PasskeyStoreSqlTextTests
{
    private static readonly Guid UserId = Guid.NewGuid();

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

        (await Passkeys(db).TryCreateAsync(passkey, UserPasskey.MaxLivePerAccount, bindCodeId: null, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.Inserted);

        var insert = db.Commands.Single();
        insert.InTransaction.Should().BeTrue();
        Squash(insert.CommandText).Should().EndWith(
            "WHERE (SELECT COUNT(*) FROM [dbo].[UserPasskeys] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = @UserId AND [RpId] = @RpId AND [RevokedAt] IS NULL) < @MaxLive",
            "of two registrations that would each be the eleventh, one is refused");
        insert.Parameters["MaxLive"].Should().Be(10);
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task TryCreate_AtTheLimit_RollsBack_AndSaysSo()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, null, CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.LimitReached);
        db.LastTransaction!.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task TryCreate_WithTheEmailedCode_SpendsItFirst_InTheSameTransaction()
    {
        var codeId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, codeId, CancellationToken.None);

        db.Commands.Should().HaveCount(2).And.OnlyContain(command => command.InTransaction);
        db.Commands[0].CommandText.Should().Be(SingleUseCodeStatements.TwoFactorBindCodes.Consume);
        db.Commands[0].Parameters["Id"].Should().Be(codeId);
        db.Commands[1].CommandText.Should().Be(UserPasskeyRepository.InsertWithinLimitSql);
    }

    [Fact]
    public async Task TryCreate_TheEmailedCodeSpentElsewhere_InsertsNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await Passkeys(db).TryCreateAsync(PasskeyTestDoubles.Live(UserId, DateTime.UtcNow), 10, Guid.NewGuid(), CancellationToken.None))
            .Should().Be(PasskeyInsertOutcome.BindCodeLost);
        db.Commands.Should().ContainSingle("the insert never runs");
        db.LastTransaction!.RolledBack.Should().BeTrue();
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

    private static WebAuthnChallengeRepository Challenges(RecordingDbConnectionFactory db) => new(db, PasskeyTestDoubles.Schema());

    private static UserPasskeyRepository Passkeys(RecordingDbConnectionFactory db) => new(db, PasskeyTestDoubles.Schema());

    private static string Squash(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
}
