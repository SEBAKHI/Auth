using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The one statement sequence that turns a proved address into an account:
/// the pending row locked, the code checked once more without charging an
/// attempt, the shared Users insert, the consumption stamp — one transaction.
/// </summary>
public class UserRepositoryCreateVerifiedTests
{
    private static readonly Guid PendingId = Guid.Parse("7d0b1c9e-6a5f-4c3d-8e2b-1f0a9c8b7d6e");

    [Fact]
    public async Task CreateVerified_ChecksTheCodeUnderTheLock_AndConsumesInOneTransaction()
    {
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT", StringComparison.Ordinal)
                ? new { Id = PendingId, OtpHash = storedHash }
                : null);

        var outcome = await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.Created);
        factory.Commands.Select(c => c.CommandText.TrimStart()[..6]).Should().Equal("SELECT", "INSERT", "UPDATE");
        factory.Commands.Should().OnlyContain(c => c.InTransaction, "every statement carries the one transaction");

        var read = factory.Commands[0];
        read.CommandText.Should().Contain("FROM [dbo].[PendingRegistrations] WITH (UPDLOCK, HOLDLOCK)");
        read.CommandText.Should().Contain("[Id] = @Id");
        read.CommandText.Should().Contain("[ConsumedAt] IS NULL");
        read.CommandText.Should().Contain("[ExpiresAt] > @Now", "expiry is enforced on this read too, by the application clock");
        read.Parameters["Id"].Should().Be(PendingId);

        var insert = factory.Commands[1];
        insert.CommandText.Should().Contain("INSERT INTO [dbo].[Users]");
        insert.Parameters["Username"].Should().Be("jane.doe@example.com");
        insert.Parameters["IsEmailConfirmed"].Should().Be(true, "the code was the proof; the row is never recorded unconfirmed");

        var consume = factory.Commands[2];
        consume.CommandText.Should().Contain("SET [ConsumedAt] = GETUTCDATE()");
        consume.CommandText.Should().Contain("[Id] = @Id AND [ConsumedAt] IS NULL");
        consume.Parameters["Id"].Should().Be(PendingId);

        factory.LastTransaction!.Committed.Should().BeTrue();
        factory.LastTransaction.RolledBack.Should().BeFalse();
    }

    [Fact]
    public async Task ACorrectCode_NeverSpendsAnAttempt_HereEither()
    {
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => new { Id = PendingId, OtpHash = storedHash });

        await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        factory.Commands.Should().OnlyContain(c => !c.CommandText.Contains("AttemptCount"),
            "the attempt gate ran in the check step; this re-check charges nothing");
    }

    [Fact]
    public async Task CreateVerified_RefusesARowThatChangedUnderTheLock_WithoutWriting()
    {
        // The stored hash no longer matches the code: rotated or consumed since
        // the caller's check. Not a guess — nothing is charged, nothing written.
        var hasher = RealHasher();
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => new { Id = PendingId, OtpHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "999999") });

        var outcome = await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.CodeRejected);
        factory.Commands.Should().HaveCount(1, "the locked read and nothing else");
        factory.LastTransaction!.RolledBack.Should().BeTrue();
        factory.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task CreateVerified_RefusesWhenNoLiveRowExists()
    {
        // The double answers 0 to the consumed-count read, so: gone or expired.
        var factory = new RecordingDbConnectionFactory(affectedRows: 0);

        var outcome = await NewRepository(factory, RealHasher())
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.CodeRejected, "gone or expired: the code is refused");
        factory.Commands.Should().HaveCount(2, "the locked read, then the consumed-or-not read; nothing written");
        factory.Commands[1].CommandText.Should().Contain("[ConsumedAt] IS NOT NULL");
        factory.Commands.Should().OnlyContain(c => c.InTransaction);
        factory.LastTransaction!.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task CreateVerified_TellsAConsumedRowApart_AsAnExistingAccount()
    {
        // The same valid form submitted twice, or another door winning the
        // race: the row is consumed, so an account exists. The caller has
        // already presented the right code and should be sent to sign in,
        // not back to a code that is spent.
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);

        var outcome = await NewRepository(factory, RealHasher())
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.DuplicateEmail);
        factory.Commands.Should().HaveCount(2);
        factory.LastTransaction!.RolledBack.Should().BeTrue("nothing is written on this path");
    }

    [Fact]
    public async Task CreateVerified_RunsOnceMore_WhenChosenAsTheDeadlockVictim()
    {
        // A check or a start landing on the row in the same few milliseconds
        // reaches it through a filtered index while this transaction holds it
        // through its clustered key and then needs that index: SQL Server
        // kills one side with 1205. The victim's transaction is gone; the
        // whole thing runs once more on a fresh connection.
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var updates = 0;
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT [Id], [OtpHash]", StringComparison.Ordinal)
                ? new { Id = PendingId, OtpHash = storedHash }
                : null,
            throwOn: command => command.CommandText.Contains("SET [ConsumedAt]", StringComparison.Ordinal) && ++updates == 1
                ? SqlExceptions.WithNumber(1205)
                : null);

        var outcome = await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.Created);
        factory.Transactions.Should().HaveCount(2, "the second attempt is its own connection and transaction");
        factory.Transactions[0].Committed.Should().BeFalse("the victim never commits");
        factory.Transactions[1].Committed.Should().BeTrue();
        factory.Commands.Select(c => c.CommandText.TrimStart()[..6]).Should().Equal("SELECT", "INSERT", "UPDATE", "SELECT", "INSERT", "UPDATE");
    }

    [Fact]
    public async Task ASecondDeadlock_Surfaces()
    {
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT [Id], [OtpHash]", StringComparison.Ordinal)
                ? new { Id = PendingId, OtpHash = storedHash }
                : null,
            throwOn: command => command.CommandText.Contains("SET [ConsumedAt]", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(1205)
                : null);

        var act = () => NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>("one retry, never a loop");
        factory.Transactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ACodeMintedForAUsersEmailVerification_IsRejectedHere()
    {
        // The same six digits hashed under a user's e-mail-verification scope
        // (the user id) do not match the pending row's own scope.
        var hasher = RealHasher();
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => new { Id = PendingId, OtpHash = hasher.Hash(Guid.NewGuid().ToString(), "123456") });

        var outcome = await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.CodeRejected);
    }

    [Fact]
    public async Task CreateVerified_MapsAUniqueViolationToDuplicateEmail_AndRollsBack()
    {
        // Another door wrote the address between the check and this insert.
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT", StringComparison.Ordinal)
                ? new { Id = PendingId, OtpHash = storedHash }
                : null,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(2627)
                : null);

        var outcome = await NewRepository(factory, hasher)
            .CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);

        outcome.Should().Be(VerifiedUserCreationOutcome.DuplicateEmail);
        factory.Commands.Should().HaveCount(2, "the read and the failed insert; the consumption never runs");
        factory.LastTransaction!.RolledBack.Should().BeTrue("the pending row is left as it was");
    }

    [Fact]
    public async Task TheInsert_IsTheOneEveryDoorUses()
    {
        // Same statement text as CreateAsync, parameter for parameter: the
        // identifier rule cannot drift between the doors.
        var hasher = RealHasher();
        var storedHash = hasher.Hash(PendingRegistration.OtpScopeFor(PendingId), "123456");
        var verified = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT", StringComparison.Ordinal)
                ? new { Id = PendingId, OtpHash = storedHash }
                : null);
        var ordinary = new RecordingDbConnectionFactory(affectedRows: 1);

        await NewRepository(verified, hasher).CreateVerifiedAsync(ConfirmedUser("jane.doe@example.com"), PendingId, "123456", CancellationToken.None);
        await NewRepository(ordinary, hasher).CreateAsync(ConfirmedUser("jane.doe@example.com"), CancellationToken.None);

        var viaVerified = verified.Commands.Single(c => c.CommandText.Contains("INSERT INTO [dbo].[Users]"));
        var viaOrdinary = ordinary.Commands.Single(c => c.CommandText.Contains("INSERT INTO [dbo].[Users]"));

        viaVerified.CommandText.Should().Be(viaOrdinary.CommandText);
        viaVerified.Parameters.Keys.Should().BeEquivalentTo(viaOrdinary.Parameters.Keys);
    }

    [Fact]
    public void EveryDapperCallInsideTheTransaction_CarriesTheTransaction()
    {
        // The recorded commands above prove it per path; this holds the whole
        // span of CreateVerifiedAsync to the rule at once, so a statement added
        // later cannot slip in on the connection alone — which SqlClient
        // refuses at runtime, as a 400 with nothing pointing at the cause.
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "UserRepository.cs"));
        var method = source.IndexOf("CreateVerifiedOnceAsync(", StringComparison.Ordinal);
        var first = source.IndexOf("BeginTransaction()", method, StringComparison.Ordinal);
        var last = source.IndexOf("transaction.Commit()", first, StringComparison.Ordinal);
        first.Should().BeGreaterThan(method);
        last.Should().BeGreaterThan(first);
        var span = source[first..last];

        var dapperCalls = Regex.Matches(span, @"connection\.(Execute|Query)\w*Async").Count;
        var withTransaction = Regex.Matches(span, @",\s*transaction\);").Count;
        var sharedInsert = Regex.Matches(span, @"InsertUserAsync\(connection, user, transaction\)").Count;

        (dapperCalls + sharedInsert).Should().Be(4, "the locked read, the consumed-or-not read, the shared insert, the consumption");
        withTransaction.Should().Be(dapperCalls + sharedInsert, "every command, direct or through the shared insert, carries the transaction");
        sharedInsert.Should().Be(1, "the insert goes through the shared statement, with the transaction");
        span.Should().NotContain("Open(", "CreateConnectionAsync returns an open connection; opening it again throws");
    }

    private static User ConfirmedUser(string email)
    {
        var user = User.Create(email, "hash", "Jane", "Doe", createdBy: Guid.Empty);
        user.ConfirmEmail(user.Id);
        return user;
    }

    private static UserRepository NewRepository(RecordingDbConnectionFactory factory, IOtpHasher hasher) => new(
        factory,
        TestHelpers.CreateOptions(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        TestHelpers.CreateOptions(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        hasher);

    private static HmacOtpHasher RealHasher()
    {
        var key = Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-bytes-long!");
        var keyService = new Mock<IRefreshTokenKeyService>();
        keyService.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(message =>
        {
            using var hmac = new HMACSHA256(key);
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        });
        return new HmacOtpHasher(keyService.Object, Mock.Of<IPasswordHasher>());
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
