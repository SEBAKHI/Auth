using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-224 V5: a change that can take the last full administrator away takes the one
/// lock, reads who holds <c>*</c>, and writes — all on one connection, in one
/// transaction, which is what makes the count and the change one step. The SQL
/// itself is the unit under test (the race on a real engine is the LocalDB probe).
/// </summary>
/// <remarks>
/// Deliberate break: pass <c>null</c> instead of the transaction in one of the
/// change's writes (a split); or read the grants before taking the lock.
/// </remarks>
public class AccountAuthorityChangeSqlTests
{
    private readonly RecordingDbConnectionFactory _db = new(affectedRows: 1);
    private readonly UserRepository _repository;

    public AccountAuthorityChangeSqlTests()
    {
        _repository = new UserRepository(
            _db,
            TestHelpers.CreateOptions(new PasswordSettings()),
            Mock.Of<IIdentifierHasher>(),
            TestHelpers.CreateOptions(new AccountDeletionSettings()),
            Mock.Of<IPerUserCryptoService>(),
            Mock.Of<IOtpHasher>());
    }

    public static TheoryData<string> Writes() => new()
    {
        "RemoveRole", "RevokePermission", "Lock", "Deactivate", "Delete",
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task EachWrite_RunsAfterTheLockAndTheCount_InOneTransaction_ThatItCommits(string write)
    {
        var userId = Guid.NewGuid();

        await using (var change = await _repository.BeginAuthorityChangeAsync(CancellationToken.None))
        {
            await WriteAsync(change, write, userId);
            change.Commit();
        }

        _db.Commands.Should().HaveCount(3, "the lock, the count and the change");
        _db.Commands.Should().OnlyContain(command => command.InTransaction,
            "a statement outside the transaction is outside the lock, and its count could be stale");
        _db.Transactions.Should().ContainSingle("one transaction holds the lock from the count to the change")
            .Which.Committed.Should().BeTrue();

        _db.Commands[0].CommandText.Should().Be(UserRepository.AccountAuthorityLockSql);
        _db.Commands[1].CommandText.Should().Be(PermissionRepository.FullAdministratorGrantsSql);
        _db.Commands[2].Parameters.Should().Contain(
            write is "Lock" or "Deactivate" ? "UserId" : write == "Delete" ? "Id" : "UserId", userId);
        _db.Commands[2].CommandText.Should().Contain(write switch
        {
            "RemoveRole" => "DELETE FROM [dbo].[UserRoles]",
            "RevokePermission" => "DELETE FROM [dbo].[UserPermissions]",
            "Delete" => "[IsDeleted] = 1",
            _ => "[Status] = @Status",
        });
    }

    [Fact]
    public void TheLock_IsOneExclusiveApplicationLock_HeldByTheTransaction_AndAFailureToGetItThrows()
    {
        var sql = UserRepository.AccountAuthorityLockSql;

        sql.Should().Contain("EXEC @Result = sp_getapplock")
            .And.Contain("@Resource = N'Auth.FullAdministrators'")
            .And.Contain("@LockMode = N'Exclusive'")
            .And.Contain("@LockOwner = N'Transaction'", "released by the commit or rollback, never left behind")
            .And.Contain("@LockTimeout = 15000");
        sql.Should().MatchRegex(@"IF @Result < 0\s+THROW 51000,",
            "a lock not granted (timeout, deadlock) is a fault, never a decision taken without it");
    }

    [Fact]
    public async Task DisposingWithoutCommit_LeavesTheTransactionUncommitted()
    {
        await using (var change = await _repository.BeginAuthorityChangeAsync(CancellationToken.None))
        {
            await change.RemoveRoleAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        }

        _db.Transactions.Should().ContainSingle().Which.Committed.Should().BeFalse(
            "a refusal or a removal that removed nothing leaves without committing, so nothing it wrote stays");
    }

    private static Task WriteAsync(IAccountAuthorityChange change, string write, Guid userId) => write switch
    {
        "RemoveRole" => change.RemoveRoleAsync(userId, Guid.NewGuid(), null, CancellationToken.None),
        "RevokePermission" => change.RevokePermissionAsync(userId, Guid.NewGuid(), null, CancellationToken.None),
        "Lock" => change.SetAccountStatusAsync(userId, AccountStatusChange.Lock(null), Guid.NewGuid(), CancellationToken.None),
        "Deactivate" => change.SetAccountStatusAsync(userId, AccountStatusChange.Deactivate(), Guid.NewGuid(), CancellationToken.None),
        "Delete" => change.DeleteAsync(userId, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, null),
    };
}
