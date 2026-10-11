using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Infrastructure;

/// <summary>The lock statements as the repository sends them, for the SQL-text tests.</summary>
internal static class LockSql
{
    /// <summary>The SQL with brackets removed and whitespace collapsed.</summary>
    public static string Normalize(string sql) =>
        Regex.Replace(sql.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    public static UserRepository Repository(RecordingDbConnectionFactory db) => new(
        db,
        Snapshot(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        Snapshot(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        Mock.Of<IOtpHasher>());

    private static IOptionsSnapshot<T> Snapshot<T>(T value) where T : class
    {
        var snapshot = new Mock<IOptionsSnapshot<T>>();
        snapshot.Setup(s => s.Value).Returns(value);
        return snapshot.Object;
    }

    /// <summary>An OUTPUT row: the lock columns before and after.</summary>
    public static object Row(int beforeStatus, int beforeCount, DateTime? beforeEnd, int afterStatus, int afterCount, DateTime? afterEnd) => new
    {
        BeforeStatus = beforeStatus,
        BeforeFailedAttempts = beforeCount,
        BeforeLockoutEndUtc = beforeEnd,
        AfterStatus = afterStatus,
        AfterFailedAttempts = afterCount,
        AfterLockoutEndUtc = afterEnd,
    };
}

/// <summary>
/// X12, LK2.1, LK3.4 and LK5: the precedence and the conditions of the lock
/// statements, held on their text. The test project has no database (as
/// SecondFactorAtomicitySqlTests explains), so the statements themselves are the
/// unit; the probe Tools/probes/account-lock.mjs runs the same statements on
/// SQL Server.
/// </summary>
/// <remarks>
/// Deliberate break: move the modulo arm (a timed lock every Max-th failure) above
/// the hard-limit arm. A failure that reaches the limit on a multiple of Max — 100
/// with Max 5 — would then start another fifteen minutes instead of the hard lock,
/// and the count would never stop at the limit.
/// </remarks>
public class LockSqlTextGuardTests
{
    private static readonly string Failed = LockSql.Normalize(UserRepository.RecordFailedLoginSql);

    private static string Case(string sql, string column)
    {
        var start = sql.IndexOf($"{column} = CASE", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the statement assigns {column} with a CASE");
        var end = sql.IndexOf(" END", start, StringComparison.Ordinal);
        return sql[start..end];
    }

    [Theory]
    [InlineData("Status")]
    [InlineData("LockoutEndUtc")]
    public void FailureStatement_TriesTheHardLimitFirst_ThenTheThreshold_ThenTheLapse(string column)
    {
        var arms = Case(Failed, column);

        var hard = arms.IndexOf("@Hard IS NOT NULL AND FailedLoginAttempts + 1 >= @Hard", StringComparison.Ordinal);
        var alreadyHard = arms.IndexOf("OR (Status = 3 AND LockoutEndUtc IS NULL)", StringComparison.Ordinal);
        var timed = arms.IndexOf("WHEN (FailedLoginAttempts + 1) % @Max = 0", StringComparison.Ordinal);
        var lapsed = arms.IndexOf("WHEN Status = 3 AND LockoutEndUtc <= SYSUTCDATETIME()", StringComparison.Ordinal);

        hard.Should().BeGreaterThanOrEqualTo(0);
        alreadyHard.Should().BeGreaterThan(hard, "an existing hard lock stays hard: a later failure never gives it an expiry");
        timed.Should().BeGreaterThan(alreadyHard, "the hard limit wins over the timed threshold");
        lapsed.Should().BeGreaterThan(timed, "a lapsed lock is repaired only when nothing locks again");
    }

    [Fact]
    public void FailureStatement_HardArm_LocksWithNoExpiry_TimedArm_ForTheDuration()
    {
        Case(Failed, "Status").Should().Contain("OR (Status = 3 AND LockoutEndUtc IS NULL) THEN 3 WHEN (FailedLoginAttempts + 1) % @Max = 0 THEN 3");
        Case(Failed, "LockoutEndUtc").Should().Contain("OR (Status = 3 AND LockoutEndUtc IS NULL) THEN NULL");
        Case(Failed, "LockoutEndUtc").Should().Contain("% @Max = 0 THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())");
        Failed.Should().StartWith("UPDATE dbo.Users SET FailedLoginAttempts = FailedLoginAttempts + 1,",
            "the count never resets in this statement: only a password proof, a reset or an administrator does");
    }

    [Fact]
    public void FailureStatement_CountsOnlyLiveRows_AndNeverAgainstAnAdministratorsLock()
    {
        var where = Failed[Failed.IndexOf(" WHERE ", StringComparison.Ordinal)..];

        where.Should().Contain("Id = @UserId");
        where.Should().Contain("IsDeleted = 0", "a pending deletion is never locked (LK6)");
        where.Should().Contain("Status IN (1, 3)", "inactive and pending accounts are not counted");
        where.Should().Contain(
            "NOT (Status = 3 AND FailedLoginAttempts = 0 AND (LockoutEndUtc IS NULL OR LockoutEndUtc > SYSUTCDATETIME()))",
            "a count above zero would make an administrator's lock read as automatic, and a familiar device may pass that");
    }

    [Fact]
    public void EveryLockStatement_ReportsThroughItsOwnOutput()
    {
        foreach (var sql in new[]
                 {
                     UserRepository.RecordFailedLoginSql, UserRepository.RecordSuccessfulPasswordLoginSql,
                     UserRepository.RecordSuccessfulLoginSql, UserRepository.ClearAutomaticLockSql,
                     UserRepository.RepairLapsedLockSql,
                 })
        {
            var text = LockSql.Normalize(sql);
            text.Should().Contain("OUTPUT deleted.Status AS BeforeStatus, deleted.FailedLoginAttempts AS BeforeFailedAttempts, deleted.LockoutEndUtc AS BeforeLockoutEndUtc, inserted.Status AS AfterStatus, inserted.FailedLoginAttempts AS AfterFailedAttempts, inserted.LockoutEndUtc AS AfterLockoutEndUtc");
            text.IndexOf(" OUTPUT ", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf(" WHERE ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LapseRepair_TouchesOnlyAnExpiredLock_AndKeepsTheCountUnlessAsked()
    {
        var sql = LockSql.Normalize(UserRepository.RepairLapsedLockSql);

        sql.Should().Contain("FailedLoginAttempts = CASE WHEN @ResetCount = 1 THEN 0 ELSE FailedLoginAttempts END");
        sql.Should().EndWith("WHERE Id = @UserId AND Status = 3 AND LockoutEndUtc <= SYSUTCDATETIME()");
    }

    [Fact]
    public void DashboardLockedCount_IncludesTheHardLock()
    {
        // LK3.4: the hard lock has no expiry, so "expiry ahead" alone never sees it.
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "DashboardStatsRepository.cs"));
        var sql = LockSql.Normalize(source);

        sql.Should().Contain(
            "WHERE IsDeleted = 0 AND (LockoutEndUtc > @Now OR (Status = 3 AND LockoutEndUtc IS NULL AND FailedLoginAttempts > 0));");
    }
}
