using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// X12, LK5 and owner decision G8: a completed sign-in lifts an automatic lock and
/// starts a new count only when it proved the password; any other sign-in writes
/// the last-login fields and nothing else. A completed reset or change shares the
/// one clearing clause. None of them touches a live administrator's lock.
/// </summary>
/// <remarks>
/// <para>
/// ARR-0638, migration step (iv): an administrator's lock written before Lock()
/// zeroed the count (7c6a0c52) can hold a count above zero, and would read as
/// automatic. The owner zeroes those before the deploy ([X12-O1]); after that the
/// clause below leaves every administrator's row alone, because it lifts only a
/// row with a count above zero or an expiry already passed. Held here on the text,
/// and on SQL Server by the probe.
/// </para>
/// <para>
/// Deliberate break (G8): ignore <c>passwordProven</c> and always send the clearing
/// statement. The false-branch assertions turn red.
/// </para>
/// </remarks>
public class SuccessRepairSqlTests
{
    private const string ClearsAutomaticOrLapsed =
        "Status = 3 AND (FailedLoginAttempts > 0 OR LockoutEndUtc <= SYSUTCDATETIME())";

    [Fact]
    public async Task PasswordProvingSignIn_LiftsTheAutomaticLock_AndStartsANewCount()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);

        await LockSql.Repository(db).RecordSuccessfulLoginAsync(Guid.NewGuid(), "203.0.113.5", passwordProven: true, CancellationToken.None);

        var sql = LockSql.Normalize(db.LastCommand!.CommandText);
        sql.Should().Contain($"Status = CASE WHEN {ClearsAutomaticOrLapsed} THEN 1 ELSE Status END");
        sql.Should().Contain($"LockoutEndUtc = CASE WHEN {ClearsAutomaticOrLapsed} THEN NULL ELSE LockoutEndUtc END");
        sql.Should().Contain("FailedLoginAttempts = 0");
        sql.Should().Contain("LastLoginUtc = SYSUTCDATETIME(), LastLoginIp = @IpAddress");
    }

    [Fact]
    public async Task OtherSignIn_WritesTheLastLoginFieldsAndNothingElse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);

        await LockSql.Repository(db).RecordSuccessfulLoginAsync(Guid.NewGuid(), "203.0.113.5", passwordProven: false, CancellationToken.None);

        var sql = LockSql.Normalize(db.LastCommand!.CommandText);
        var set = sql[..sql.IndexOf(" OUTPUT ", StringComparison.Ordinal)];
        set.Should().Be("UPDATE dbo.Users SET LastLoginUtc = SYSUTCDATETIME(), LastLoginIp = @IpAddress, ModifiedAt = SYSUTCDATETIME()");
        set.Should().NotContain("Status").And.NotContain("FailedLoginAttempts").And.NotContain("LockoutEndUtc");
    }

    [Fact]
    public async Task ClearAutomaticLock_SharesTheClause_SparesDeletedRows_AndTheSecondFactor()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);

        await LockSql.Repository(db).ClearAutomaticLockAsync(Guid.NewGuid(), CancellationToken.None);

        var sql = LockSql.Normalize(db.LastCommand!.CommandText);
        sql.Should().Contain(LockSql.Normalize(UserRepositoryClause()));
        sql.Should().EndWith("WHERE Id = @UserId AND IsDeleted = 0");
        sql.Should().NotContain("TwoFactorAuth", "a password reset never clears the second factor's counter");
    }

    [Fact]
    public void Clause_LeavesALiveAdministratorsLock_AfterMigrationStepFour()
    {
        // The administrator's lock is count 0 with no expiry or one ahead: neither
        // "count above zero" nor "expiry passed" matches it, so its status and its
        // expiry stay; the count it already holds (0) is written again.
        var clause = LockSql.Normalize(UserRepositoryClause());

        clause.Should().Contain($"WHEN {ClearsAutomaticOrLapsed} THEN 1 ELSE Status END");
        clause.Should().NotContain("FailedLoginAttempts >= ", "the old rule read a count at the threshold as automatic");
        clause.Should().NotContain("LockoutEndUtc IS NULL", "a missing expiry alone never marks a lock as automatic or administrative");
    }

    [Fact]
    public async Task SignIn_ReportsTheLockItLifted()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => LockSql.Row((int)UserStatus.Locked, 100, null, (int)UserStatus.Active, 0, null));

        var transition = await LockSql.Repository(db).RecordSuccessfulLoginAsync(
            Guid.NewGuid(), null, passwordProven: true, CancellationToken.None);

        transition!.LiftedAutomaticLock(DateTime.UtcNow).Should().BeTrue();
    }

    /// <summary>The shared clause, as the repository declares it.</summary>
    private static string UserRepositoryClause()
    {
        var field = typeof(UserRepository).GetField(
            "AutomaticLockClearSetClause",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field.Should().NotBeNull("the clearing clause is declared once (DRY)");
        return (string)field!.GetRawConstantValue()!;
    }
}
