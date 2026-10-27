using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-215 V5, V6, V11: the statements behind the sign-in by emailed code. A code is
/// spent under the row's lock, re-checked without a charge, so it signs in once; the
/// first confirmation and the owner's removal each write only over the state their
/// request read. The SQL is the unit under test. The race on a real engine (one code,
/// two sign-ins at once: exactly one wins) was run against throwaway LocalDB with a
/// scratch console over the real repository in REVIEW 110 (F7), with its deliberate
/// breaks; it is not committed.
/// </summary>
/// <remarks>
/// Deliberate break: drop <c>[ConsumedAt] IS NULL</c> from the consume's read, or the
/// <c>[IsEmailConfirmed] = 0</c> guard from the confirmation.
/// </remarks>
public class EmailCodeSignInSqlTests
{
    private static readonly Guid RowId = Guid.NewGuid();

    [Fact]
    public async Task TryConsumeSignInCode_LocksTheLiveRow_RechecksWithoutACharge_AndStampsItConsumed()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: command =>
            command.CommandText.Contains("SELECT [OtpHash]", StringComparison.Ordinal) ? new { OtpHash = "stored" } : null);
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Verify(PendingRegistration.OtpScopeFor(RowId), "123456", "stored")).Returns(true);

        var spent = await Repository(db, hasher.Object).TryConsumeSignInCodeAsync(RowId, "123456", CancellationToken.None);

        spent.Should().BeTrue();
        db.Commands.Should().HaveCount(2);
        var read = db.Commands[0].CommandText;
        read.Should().Contain("WITH (UPDLOCK, HOLDLOCK)").And.Contain("[ConsumedAt] IS NULL").And.Contain("[ExpiresAt] > @Now",
            "a consumed or dead row signs nobody in, and the lock holds it to the commit");
        db.Commands[1].CommandText.Should().Contain("SET [ConsumedAt] = GETUTCDATE()").And.Contain("[ConsumedAt] IS NULL");
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Commands.Should().NotContain(command => command.CommandText.Contains("[AttemptCount]"),
            "the re-check is not a guess: nothing is charged");
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task TryConsumeSignInCode_WhenTheRowNoLongerHoldsTheCode_WritesNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new { OtpHash = "rotated" });
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var spent = await Repository(db, hasher.Object).TryConsumeSignInCodeAsync(RowId, "123456", CancellationToken.None);

        spent.Should().BeFalse();
        db.Commands.Should().ContainSingle("the read alone; no stamp");
        db.Transactions.Single().RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task TryConsumeSignInCode_WithNoLiveRow_WritesNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var spent = await Repository(db, Mock.Of<IOtpHasher>()).TryConsumeSignInCodeAsync(RowId, "123456", CancellationToken.None);

        spent.Should().BeFalse("a code spent by the first of two sign-ins finds no unconsumed row for the second");
        db.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task ConfirmEmailBySignInCode_WritesOnlyOverTheStateTheSignInRead_AndLeavesTheChangeStamp()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var confirmed = await Users(db).ConfirmEmailBySignInCodeAsync(Guid.NewGuid(), "read-hash", CancellationToken.None);

        confirmed.Should().BeTrue();
        var sql = db.LastCommand!.CommandText;
        sql.Should().Contain("[IsEmailConfirmed] = 1").And.Contain("[PasswordHash] = NULL").And.Contain("[MustChangePassword] = 0");
        sql.Should().Contain("[IsEmailConfirmed] = 0", "only the first confirmation retires a password");
        sql.Should().Contain("[PasswordHash] = @ReadPasswordHash").And.Contain("[PasswordHash] IS NULL AND @ReadPasswordHash IS NULL");
        sql.Should().NotContain("[LastPasswordChangeUtc]", "the mailbox was proven, never a password");
        db.LastCommand.Parameters["ReadPasswordHash"].Should().Be("read-hash");
    }

    [Fact]
    public async Task ConfirmEmailBySignInCode_WhenTheRowChanged_AnswersFalse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await Users(db).ConfirmEmailBySignInCodeAsync(Guid.NewGuid(), null, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task RemovePassword_RemovesAndRecordsTheHash_InOneCommittedTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var userId = Guid.NewGuid();

        var removed = await Users(db).RemovePasswordAsync(userId, "read-hash", userId, CancellationToken.None);

        removed.Should().BeTrue();
        db.Commands.Should().HaveCount(2);
        db.Commands[0].CommandText.Should().Contain("[PasswordHash] = NULL").And.Contain("[LastPasswordChangeUtc] = GETUTCDATE()")
            .And.Contain("[PasswordHash] = @ReadPasswordHash").And.Contain("[IsEmailConfirmed] = 1");
        db.Commands[1].CommandText.Should().Contain("INSERT INTO [dbo].[PasswordHistory]");
        db.Commands[1].Parameters["PasswordHash"].Should().Be("read-hash");
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task RemovePassword_WhenThePasswordChanged_RecordsNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var removed = await Users(db).RemovePasswordAsync(Guid.NewGuid(), "read-hash", Guid.NewGuid(), CancellationToken.None);

        removed.Should().BeFalse();
        db.Commands.Should().ContainSingle("no history row for a removal that did not happen");
        db.Transactions.Single().RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task CountEmailCodeOnlyAccounts_CountsLiveAccountsWithNoPasswordAndNoProvider()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 4);

        (await Users(db).CountEmailCodeOnlyAccountsAsync(CancellationToken.None)).Should().Be(4);

        var sql = db.LastCommand!.CommandText;
        sql.Should().Contain("COUNT(*)");
        sql.Should().Contain("[PasswordHash] IS NULL").And.Contain("[IsDeleted] = 0")
            .And.Contain("NOT EXISTS (SELECT 1 FROM [dbo].[UserExternalLogins]");
        db.LastCommand.Parameters["SystemUserId"].Should().Be(WellKnownUserIds.System);
    }

    private static PendingRegistrationRepository Repository(RecordingDbConnectionFactory db, IOtpHasher hasher) =>
        new(db, Mock.Of<IOtpGenerator>(), hasher, TestHelpers.CreateOptions(new EmailSettings()));

    private static UserRepository Users(RecordingDbConnectionFactory db) => new(
        db,
        TestHelpers.CreateOptions(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        TestHelpers.CreateOptions(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        Mock.Of<IOtpHasher>());
}
