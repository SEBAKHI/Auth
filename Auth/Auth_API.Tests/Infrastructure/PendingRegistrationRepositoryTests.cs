using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// What the pending-registration repository asks the database, under what
/// lock, and what it decides from the row it gets back. The test project has
/// no database; the recording double answers each read with the row a test
/// hands it, and the commands the repository issues in response are what is
/// asserted on.
/// </summary>
public class PendingRegistrationRepositoryTests
{
    private static readonly PendingRegistrationStartRequest Request = new(
        Handle: "handle-1",
        Email: " Jane@One.Example ",
        PreferredLanguage: "ar",
        ExpirationMinutes: 5,
        MailWindow: TimeSpan.FromSeconds(60),
        MaxMailsPerWindow: 3);

    private static readonly Guid RowId = Guid.Parse("7d0b1c9e-6a5f-4c3d-8e2b-1f0a9c8b7d6e");

    // ── StartAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_OnAnAddressWithNoRow_LocksTheGap_InsertsAndMintsACode_InOneTransaction()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        var generator = new Mock<IOtpGenerator>();
        generator.Setup(g => g.GenerateNumericOtp(6)).Returns("123456");
        var scopes = new List<string>();
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>(), "123456"))
            .Callback<string, string>((scope, _) => scopes.Add(scope))
            .Returns("hash-of-123456");

        var result = await new PendingRegistrationRepository(factory, generator.Object, hasher.Object)
            .StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Minted);
        result.Code.Should().Be("123456", "the plaintext exists only here, for the caller to mail");
        result.Row.OtpHash.Should().Be("hash-of-123456");
        result.Row.MailedCount.Should().Be(1, "the first code charges the window like every later one");
        result.Row.WasMailed.Should().BeFalse("the outbox has not been written yet");
        scopes.Should().Equal($"pending-registration:{result.Row.Id}");

        var read = factory.Commands[0];
        read.CommandText.Should().Contain("WITH (UPDLOCK, HOLDLOCK)");
        read.CommandText.Should().Contain("[ConsumedAt] IS NULL");
        read.CommandText[read.CommandText.IndexOf("WHERE", StringComparison.Ordinal)..].Should().NotContain("ExpiresAt",
            "an expired row is still the address's one unconsumed row: it must be found and rotated, not collided with");
        read.Parameters["NormalizedEmail"].Should().Be("JANE@ONE.EXAMPLE");
        read.InTransaction.Should().BeTrue();

        var insert = factory.Commands[1];
        insert.CommandText.Should().Contain("INSERT INTO [dbo].[PendingRegistrations]");
        insert.InTransaction.Should().BeTrue();
        insert.Parameters["Handle"].Should().Be("handle-1");
        insert.Parameters["Email"].Should().Be("jane@one.example");
        insert.Parameters["NormalizedEmail"].Should().Be("JANE@ONE.EXAMPLE");
        insert.Parameters["OtpHash"].Should().Be("hash-of-123456");
        insert.Parameters["MailedAt"].Should().BeNull();
        insert.Parameters["ConsumedAt"].Should().BeNull();

        factory.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_LooksTheRowUp_ByTheSameKeyTheEntityStoresIt_Under()
    {
        // Two derivations of "the normalized address" would let the lock be
        // taken on one key while the row sits under another. One function.
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);

        await Repository(factory).StartAsync(Request with { Email = "  Ünïcode.User@Example.COM " }, CancellationToken.None);

        var expected = PendingRegistration.Create("h", "  Ünïcode.User@Example.COM ", DateTime.UtcNow).NormalizedEmail;
        factory.Commands[0].Parameters["NormalizedEmail"].Should().Be(expected);
        factory.Commands[1].Parameters["NormalizedEmail"].Should().Be(expected);
    }

    [Fact]
    public async Task Start_OnALiveMailedCode_LeavesItAlone()
    {
        // The code is in someone's inbox. Re-minting would invalidate what they
        // are about to type; re-mailing would let a stranger flood the address.
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true));
        var generator = new Mock<IOtpGenerator>(MockBehavior.Strict);

        var result = await new PendingRegistrationRepository(factory, generator.Object, Mock.Of<IOtpHasher>())
            .StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Unchanged);
        result.Code.Should().BeNull("nothing new was minted, so there is nothing to mail");
        result.Row.Id.Should().Be(RowId);
        result.Row.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(4), TimeSpan.FromSeconds(5),
            "the caller reports the stored expiry, not a fresh one");
        factory.Commands.Should().HaveCount(1, "a SELECT and nothing else");
        factory.LastTransaction!.Committed.Should().BeTrue();
        factory.LastTransaction.RolledBack.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, true, 0, "an expired code")]
    [InlineData(4, true, PendingRegistration.MaxAttempts, "an exhausted code")]
    [InlineData(4, false, 0, "a live code that never reached the outbox")]
    public async Task Start_OnADeadOrUnmailedCode_RotatesItInPlace(int expiresInMinutes, bool mailed, int attempts, string because)
    {
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(expiresInMinutes), mailed: mailed, attempts: attempts,
                mailedCount: 1, windowStartedAgo: TimeSpan.FromSeconds(30), handle: "handle-under-the-old-key"));
        var generator = new Mock<IOtpGenerator>();
        generator.Setup(g => g.GenerateNumericOtp(6)).Returns("654321");
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Hash($"pending-registration:{RowId}", "654321")).Returns("hash-of-654321");

        var result = await new PendingRegistrationRepository(factory, generator.Object, hasher.Object)
            .StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Minted, because);
        result.Code.Should().Be("654321");
        result.Row.Id.Should().Be(RowId, "rotation keeps the row");
        result.Row.MailedCount.Should().Be(2, "the address's mail counter survives the rotation while its window is open");
        result.Row.Handle.Should().Be("handle-1", "the handle is re-keyed with every code");

        factory.Commands.Should().HaveCount(2);
        var update = factory.Commands[1];
        update.CommandText.Should().Contain("UPDATE [dbo].[PendingRegistrations]");
        update.CommandText.Should().NotContain("INSERT", "in place, never a second row");
        update.CommandText.Should().Contain("[AttemptCount] = 0");
        update.CommandText.Should().Contain("[MailedAt] = NULL");
        update.CommandText.Should().Contain("[VerifiedAt] = NULL");
        update.CommandText.Should().Contain("[Handle] = @Handle");
        update.CommandText.Should().Contain("[ConsumedAt] IS NULL", "a consumed row is never rotated");
        update.CommandText.Should().NotContain("[ConsumedAt] =", "rotation never touches ConsumedAt");
        update.InTransaction.Should().BeTrue();
        update.Parameters["Id"].Should().Be(RowId);
        update.Parameters["Handle"].Should().Be("handle-1");
        update.Parameters["OtpHash"].Should().Be("hash-of-654321");
        update.Parameters["MailedCount"].Should().Be(2);
        update.Parameters["PreferredLanguage"].Should().Be("ar");
        factory.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_OnASpentMailWindow_MintsNothing_AndWritesNothing()
    {
        // Three codes in one minute is the cap. A dead code past the cap stays
        // dead until the window opens again; the guessing bound is the point.
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(-1), mailed: true,
                mailedCount: Request.MaxMailsPerWindow, windowStartedAgo: TimeSpan.FromSeconds(30)));
        var generator = new Mock<IOtpGenerator>(MockBehavior.Strict);

        var result = await new PendingRegistrationRepository(factory, generator.Object, Mock.Of<IOtpHasher>())
            .StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Unchanged);
        result.Code.Should().BeNull();
        result.Row.MailedCount.Should().Be(Request.MaxMailsPerWindow, "a refused charge changes nothing");
        factory.Commands.Should().HaveCount(1);
        factory.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_WhenTheInsertCollides_RollsBack_AndStartsOverAgainstTheWinnersRow_Once()
    {
        // The one race the key-range lock does not close on every isolation
        // level. The loser must not surface a 409 to a client that did nothing
        // wrong: it re-reads and finds the winner's row.
        var reads = 0;
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.Contains("SELECT", StringComparison.Ordinal) && ++reads == 2
                ? Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true)
                : null,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(2627)
                : null);
        var generator = new Mock<IOtpGenerator>();
        generator.Setup(g => g.GenerateNumericOtp(6)).Returns("123456");

        var result = await new PendingRegistrationRepository(factory, generator.Object, MintingHasher())
            .StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Unchanged, "the winner's code is live and mailed");
        result.Row.Id.Should().Be(RowId);
        factory.Commands.Select(c => c.CommandText.TrimStart()[..6]).Should().Equal("SELECT", "INSERT", "SELECT");
        factory.Transactions.Should().HaveCount(2, "each attempt is its own connection and transaction");
        factory.Transactions[0].RolledBack.Should().BeTrue("the failed insert is undone before the retry");
        factory.Transactions[0].Committed.Should().BeFalse();
        factory.Transactions[1].Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_WhenTheInsertCollidesTwice_LetsTheSecondOneSurface()
    {
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                ? SqlExceptions.WithNumber(2601)
                : null);
        var generator = new Mock<IOtpGenerator>();
        generator.Setup(g => g.GenerateNumericOtp(6)).Returns("123456");

        var act = () => new PendingRegistrationRepository(factory, generator.Object, MintingHasher())
            .StartAsync(Request, CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>("one retry, never a loop");
        factory.Commands.Count(c => c.CommandText.Contains("INSERT", StringComparison.Ordinal)).Should().Be(2);
    }

    // ── CheckCodeUnderLockAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CheckCode_ReadsUnderTheLock_EnforcesExpiryOnTheRead_AndAnswersNotFoundForNoRow()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 0);

        var result = await Repository(factory).CheckCodeUnderLockAsync("handle-1", "123456", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.NotFound);
        result.Row.Should().BeNull();

        var read = factory.Commands.Single();
        read.CommandText.Should().Contain("WITH (UPDLOCK, HOLDLOCK)",
            "the attempt gate and the hash comparison must sit under the same lock");
        read.CommandText.Should().Contain("[Handle] = @Handle");
        read.CommandText.Should().Contain("[ConsumedAt] IS NULL");
        read.CommandText.Should().Contain("[ExpiresAt] > @Now", "expiry is enforced on every read, never by the sweep");
        read.CommandText.Should().NotContain("GETUTCDATE", "the clock that minted ExpiresAt is the one that judges it");
        ((DateTime)read.Parameters["Now"]!).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        read.InTransaction.Should().BeTrue();
        factory.LastTransaction!.RolledBack.Should().BeTrue("nothing was written");
    }

    [Fact]
    public async Task CheckCode_OnAWrongCode_ChargesAnAttempt_AndCommitsIt()
    {
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true, attempts: 2));
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Verify($"pending-registration:{RowId}", "000000", "hash-of-123456")).Returns(false);

        var result = await Repository(factory, hasher.Object).CheckCodeUnderLockAsync("handle-1", "000000", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.Wrong);
        result.Row!.AttemptCount.Should().Be(3, "the caller sees the count after the charge");
        result.Row.IsConsumed.Should().BeFalse();

        var update = factory.Commands[1];
        update.CommandText.Should().Contain("SET [AttemptCount] = [AttemptCount] + 1",
            "the increment is relative, so concurrent guesses cannot overwrite each other's charge");
        update.CommandText.Should().NotContain("ConsumedAt", "a wrong guess kills the code eventually, never the row");
        update.Parameters["Id"].Should().Be(RowId);
        update.InTransaction.Should().BeTrue();
        factory.LastTransaction!.Committed.Should().BeTrue("a rolled-back increment would hand the guess back");
    }

    [Fact]
    public async Task CheckCode_OnAnExhaustedCode_RollsBack_WithoutHashing()
    {
        // Five wrong guesses already. The hash is not computed and no attempt
        // is charged: the sixth guess is refused by the gate, not by comparison.
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true, attempts: PendingRegistration.MaxAttempts));
        var hasher = new Mock<IOtpHasher>(MockBehavior.Strict);

        var result = await Repository(factory, hasher.Object).CheckCodeUnderLockAsync("handle-1", "123456", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.Exhausted);
        result.Row!.Id.Should().Be(RowId);
        factory.Commands.Should().HaveCount(1, "a SELECT and nothing else");
        factory.LastTransaction!.RolledBack.Should().BeTrue();
        factory.LastTransaction.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task CheckCode_OnTheRightCode_StampsVerified_AndConsumesNothing()
    {
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true, attempts: 4));
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Verify($"pending-registration:{RowId}", "123456", "hash-of-123456")).Returns(true);

        var result = await Repository(factory, hasher.Object).CheckCodeUnderLockAsync("handle-1", "123456", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.Match);
        result.Row!.VerifiedAt.Should().NotBeNull();
        result.Row.IsConsumed.Should().BeFalse("only the completion step, having written the account, consumes");
        result.Row.AttemptCount.Should().Be(4, "a right code costs nothing");

        var update = factory.Commands[1];
        update.CommandText.Should().Contain("SET [VerifiedAt] = COALESCE([VerifiedAt], GETUTCDATE())");
        update.CommandText.Should().NotContain("ConsumedAt");
        update.CommandText.Should().NotContain("AttemptCount");
        update.InTransaction.Should().BeTrue();
        factory.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task ACodeMintedForAUsersEmailVerification_IsRejectedHere()
    {
        // The real hasher, so the scope separation is proved rather than
        // mocked: a code hashed under a user's id (the e-mail verification
        // scope) does not verify against a pending row's scope, even for the
        // same six digits, and even if the two hashes were somehow swapped.
        var hasher = RealHasher();
        var userId = Guid.NewGuid();
        var storedForTheUser = hasher.Hash(userId.ToString(), "123456");
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true, otpHash: storedForTheUser));

        var result = await Repository(factory, hasher).CheckCodeUnderLockAsync("handle-1", "123456", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.Wrong,
            "the pending row's scope is its own id; a digest computed under any other scope is a wrong code here");
        hasher.Verify($"pending-registration:{RowId}", "123456", hasher.Hash($"pending-registration:{RowId}", "123456"))
            .Should().BeTrue("the same hasher accepts the code under the row's own scope");
    }

    [Fact]
    public async Task CheckCode_RunsOnceMore_WhenChosenAsTheDeadlockVictim()
    {
        // The completion step holds the row through its clustered key and then
        // needs the Handle index this read comes in by; a check landing in the
        // same few milliseconds can be the deadlock victim. Nothing of ours was
        // written, so the read is run once more on a fresh connection.
        var reads = 0;
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => Row(expiresIn: TimeSpan.FromMinutes(4), mailed: true),
            throwOn: command => command.CommandText.Contains("SELECT", StringComparison.Ordinal) && ++reads == 1
                ? SqlExceptions.WithNumber(1205)
                : null);
        var hasher = new Mock<IOtpHasher>();
        hasher.Setup(h => h.Verify($"pending-registration:{RowId}", "123456", "hash-of-123456")).Returns(true);

        var result = await Repository(factory, hasher.Object).CheckCodeUnderLockAsync("handle-1", "123456", CancellationToken.None);

        result.Outcome.Should().Be(PendingRegistrationCodeOutcome.Match);
        factory.Transactions.Should().HaveCount(2, "the second attempt is its own connection and transaction");
        factory.Transactions[1].Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Start_RunsOnceMore_WhenChosenAsTheDeadlockVictim()
    {
        var inserts = 0;
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("INSERT", StringComparison.Ordinal) && ++inserts == 1
                ? SqlExceptions.WithNumber(1205)
                : null);

        var result = await Repository(factory).StartAsync(Request, CancellationToken.None);

        result.Action.Should().Be(PendingRegistrationStartAction.Minted);
        factory.Transactions.Should().HaveCount(2);
        factory.Transactions[1].Committed.Should().BeTrue();
    }

    // ── The rest ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkMailed_StampsOnlyTheCodeThatWasMailed()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        var id = Guid.NewGuid();

        await Repository(factory).MarkMailedAsync(id, "hash-of-123456", CancellationToken.None);

        var update = factory.Commands.Single();
        update.CommandText.Should().Contain("SET [MailedAt] = GETUTCDATE()");
        update.CommandText.Should().Contain("[OtpHash] = @OtpHash",
            "a rotation between the commit and the enqueue must not be stamped as mailed");
        update.CommandText.Should().Contain("[MailedAt] IS NULL");
        update.Parameters["Id"].Should().Be(id);
        update.Parameters["OtpHash"].Should().Be("hash-of-123456");
    }

    [Fact]
    public async Task ConsumeByEmail_StampsTheAddressesUnconsumedRow()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);

        var changed = await Repository(factory).ConsumeByEmailAsync("JANE@ONE.EXAMPLE", CancellationToken.None);

        changed.Should().Be(1);
        var update = factory.Commands.Single();
        update.CommandText.Should().Contain("SET [ConsumedAt] = GETUTCDATE()");
        update.CommandText.Should().Contain("[NormalizedEmail] = @NormalizedEmail");
        update.CommandText.Should().Contain("[ConsumedAt] IS NULL");
        update.Parameters["NormalizedEmail"].Should().Be("JANE@ONE.EXAMPLE");
    }

    [Fact]
    public async Task CleanupExpired_DeletesOneBoundedBatch_ByExpiry_ConsumedOrNot()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 7);
        var cutoff = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

        var deleted = await Repository(factory).CleanupExpiredAsync(cutoff, 4000, CancellationToken.None);

        deleted.Should().Be(7);
        var delete = factory.Commands.Single();
        delete.CommandText.Should().Contain("DELETE TOP (@BatchSize) FROM [dbo].[PendingRegistrations]");
        delete.CommandText.Should().Contain("[ExpiresAt] < @OlderThan");
        delete.CommandText.Should().NotContain("ConsumedAt", "a consumed row is history too, and it expires like the rest");
        delete.Parameters["BatchSize"].Should().Be(4000);
        delete.Parameters["OlderThan"].Should().Be(cutoff);
    }

    [Fact]
    public void EveryDapperCallInsideATransaction_CarriesTheTransaction_AndNothingReopensTheConnection()
    {
        // The recorded commands above prove InTransaction per path they reach;
        // this holds the whole transactional span to the same rule at once, so
        // a new statement added later cannot slip in without the transaction.
        // A Dapper call issued on a connection with an open transaction it was
        // not handed makes SqlClient throw, and the middleware turns that into
        // a 400 with nothing pointing at the cause.
        var source = RepositorySource();
        var first = source.IndexOf("BeginTransaction()", StringComparison.Ordinal);
        var last = source.LastIndexOf("transaction.Commit()", StringComparison.Ordinal);
        first.Should().BePositive();
        last.Should().BeGreaterThan(first);
        var span = source[first..last];

        var dapperCalls = Regex.Matches(span, @"connection\.(Execute|Query)\w*Async").Count;
        var withTransaction = Regex.Matches(span, @",\s*transaction\);").Count;

        dapperCalls.Should().BeGreaterThanOrEqualTo(4, "the start and check methods issue at least a read and a write each");
        withTransaction.Should().Be(dapperCalls, "every command inside the transaction must carry it");
        span.Should().NotContain("Open(", "CreateConnectionAsync returns an open connection; opening it again throws");
    }

    [Fact]
    public void OnlyTwoStatementsInTheApplication_WriteConsumedAt()
    {
        // ConsumedAt means one thing: a Users row now exists for the address.
        // Two places can say so — the by-address consumer every other door
        // runs after its insert, and the verified creation that inserts and
        // consumes in one transaction. Starting, checking and rotating never do.
        Regex.Matches(RepositorySource(), @"SET \[ConsumedAt\]").Count.Should().Be(1,
            "in this repository only ConsumeByEmailAsync consumes");

        var persistence = Path.Combine(SolutionDirectory(), "Auth.Infrastructure", "Persistence");
        var writers = Directory.GetFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"SET \[ConsumedAt\]").Select(_ => Path.GetFileName(file)))
            .ToList();

        writers.Should().BeEquivalentTo(["PendingRegistrationRepository.cs", "UserRepository.cs"],
            "the other writer is UserRepository.CreateVerifiedAsync, in the same transaction as the account row");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static PendingRegistrationRepository Repository(RecordingDbConnectionFactory factory, IOtpHasher? hasher = null) =>
        new(factory, MintingGenerator(), hasher ?? MintingHasher());

    private static IOtpGenerator MintingGenerator() =>
        Mock.Of<IOtpGenerator>(g => g.GenerateNumericOtp(6) == "123456");

    private static IOtpHasher MintingHasher() =>
        Mock.Of<IOtpHasher>(h => h.Hash(It.IsAny<string>(), It.IsAny<string>()) == "hash-of-123456");

    /// <summary>The row the double answers a read with, shaped like the repository's DTO.</summary>
    private static object Row(
        TimeSpan expiresIn,
        bool mailed,
        int attempts = 0,
        int mailedCount = 1,
        TimeSpan? windowStartedAgo = null,
        string handle = "handle-1",
        string otpHash = "hash-of-123456")
    {
        var now = DateTime.UtcNow;
        return new
        {
            Id = RowId,
            Handle = handle,
            Email = "jane@one.example",
            NormalizedEmail = "JANE@ONE.EXAMPLE",
            OtpHash = otpHash,
            ExpiresAt = now.Add(expiresIn),
            AttemptCount = attempts,
            MailedCount = mailedCount,
            MailWindowStartUtc = now - (windowStartedAgo ?? TimeSpan.FromMinutes(10)),
            MailedAt = mailed ? now.AddMinutes(-1) : (DateTime?)null,
            VerifiedAt = (DateTime?)null,
            ConsumedAt = (DateTime?)null,
            PreferredLanguage = (string?)"en",
            CreatedAt = now.AddMinutes(-10)
        };
    }

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

    private static string RepositorySource() => File.ReadAllText(Path.Combine(
        SolutionDirectory(), "Auth.Infrastructure", "Persistence", "PendingRegistrationRepository.cs"));

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
