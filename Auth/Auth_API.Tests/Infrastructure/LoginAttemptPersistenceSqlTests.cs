using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the SQL behind "one row per sign-in ceremony".
///
/// The three states a login attempt can be in are encoded in columns rather than
/// in a status column: a ceremony still waiting on its second factor is the
/// otherwise-unreachable combination of IsSuccessful = 0 with no FailureReason.
/// That encoding is only worth anything if every reader honours it, and the
/// readers are raw Dapper SQL that no handler test can see. So the SQL text is
/// the unit under test, the same approach SessionPersistenceSqlTests takes.
///
/// The specific regression this exists for: counting an unfinished ceremony as a
/// failed sign-in, which is what painted every clean two-factor login red.
/// </summary>
public class LoginAttemptPersistenceSqlTests
{
    [Fact]
    public void TheInsertCarriesTheCeremonyLink()
    {
        // Without the column on the way in there is nothing to settle later, and
        // the verify step would silently fall back to leaving a second row.
        var repository = ReadLoginAttemptRepository();

        repository.Should().Contain("[TwoFactorChallengeId]");
        repository.Should().Contain("@TwoFactorChallengeId");
        repository.Should().Contain("attempt.TwoFactorChallengeId");
    }

    [Fact]
    public void TheResolveUpdateTouchesOnlyAnOpenCeremony()
    {
        // This predicate is the whole concurrency and idempotency story. Without
        // it a retry, a duplicate verify, or a late session-limit refusal would
        // overwrite an outcome that was already recorded.
        var resolve = MethodBody(
            ReadLoginAttemptRepository(),
            "public async Task ResolveTwoFactorCeremonyAsync",
            "public async Task<IReadOnlyList<SignInHistoryEntry>> GetSignInHistoryAsync");

        resolve.Should().Contain("UPDATE [dbo].[LoginAttempts]");
        resolve.Should().Contain("WHERE [TwoFactorChallengeId] = @ChallengeId");
        resolve.Should().Contain("AND [IsSuccessful] = 0");
        resolve.Should().Contain("AND [FailureReason] IS NULL");
    }

    [Fact]
    public void TheHistoryQueryToleratesAMissingChallengeRow()
    {
        // Challenges are purged on a far shorter policy than attempts. An INNER
        // JOIN would quietly drop older sign-ins out of the user's own history.
        var history = MethodBody(
            ReadLoginAttemptRepository(),
            "public async Task<IReadOnlyList<SignInHistoryEntry>> GetSignInHistoryAsync",
            "public async Task<IReadOnlyList<LoginAttempt>> GetRecentByEmailAsync");

        history.Should().Contain("LEFT JOIN [dbo].[TwoFactorChallenges]");
        history.Should().Contain("ISNULL(ch.[AttemptCount], 0)");
        history.Should().NotContain("INNER JOIN");
    }

    [Fact]
    public void EveryDashboardFailureCountExcludesUnfinishedCeremonies()
    {
        // A failure is a row that failed AND says why. Any bare [IsSuccessful] = 0
        // in the stats query counts ceremonies that are merely waiting, which is
        // the defect: for a tenant on two-factor it inflates every failure number
        // by roughly one per successful sign-in.
        var stats = MethodBody(
            ReadDashboardRepository(),
            "public async Task<AuthStatsSnapshot> GetAuthStatsAsync",
            "private static string ToSqlServerTimeZone");

        var unguarded = Regex.Matches(stats, @"\[IsSuccessful\]\s*=\s*0(?!\s*AND\s+(la\.)?\[FailureReason\] IS NOT NULL)")
            .Select(match => Line(stats, match.Index))
            .Where(line => !line.Contains("TOP (10)", StringComparison.Ordinal))
            .ToList();

        // The top-failing-IP query is the one deliberate exception and is asserted
        // separately below; it is identified by its own predicate, not by position.
        unguarded
            .Where(line => !IsTopFailingIpPredicate(stats, line))
            .Should()
            .BeEmpty("every failure aggregate must require a reason");
    }

    [Fact]
    public void TheTopFailingIpQueryStillCountsUnfinishedCeremonies()
    {
        // Deliberately unlike every other aggregate. This feed drives the only
        // automated attack alert in the product, and an address that produced
        // correct passwords for ten accounts and stopped at the second factor is
        // the strongest signal it carries. Excluding those because they are not
        // technically failures would let an attacker go dark by using credentials
        // that work.
        var stats = MethodBody(
            ReadDashboardRepository(),
            "public async Task<AuthStatsSnapshot> GetAuthStatsAsync",
            "private static string ToSqlServerTimeZone");

        var topIps = Section(stats, "-- Top failing IP addresses", "-- Outcomes per application");

        topIps.Should().Contain("[IsSuccessful] = 0");
        topIps.Should().NotContain("[FailureReason] IS NOT NULL");
    }

    [Fact]
    public void ThePerAddressCeilingCountsOnlyWrongPasswords()
    {
        // Refusals ("Account locked", "Source locked") and open ceremonies are rows
        // too. Counting them would let each refused retry re-arm the window, and a
        // shared address could then be kept refused for ever by one request per
        // window.
        var count = MethodBody(
            ReadLoginAttemptRepository(),
            "public async Task<int> CountFailedAttemptsForUserFromIpAsync",
            "public async Task<bool> HasSucceededFromAsync");

        count.Should().Contain("AND [IsSuccessful] = 0");
        count.Should().Contain("AND [FailureReason] = @WrongPassword");
        count.Should().Contain("LoginFailureReasons.InvalidPassword");
    }

    [Fact]
    public void FamiliarityByDeviceRequiresALiveSession()
    {
        // Session rows are kept for history. A device the owner forgot or an
        // administrator revoked must not stay familiar for the life of the table.
        var familiar = MethodBody(
            ReadLoginAttemptRepository(),
            "public async Task<bool> HasSucceededFromAsync",
            "public async Task CleanupOldAttemptsAsync");

        familiar.Should().Contain("AND [IsSuccessful] = 1");
        familiar.Should().Contain("AND [EndedAt] IS NULL");
        familiar.Should().Contain("AND [ExpiresAt] > GETUTCDATE()");
    }

    [Fact]
    public void TheReasonConstantsStillSpellTheLiteralsTheReadersMatch()
    {
        // The dashboard matches N'Account locked' as prose; the per-address ceiling
        // matches the constant. Both stay true only while the constants keep the
        // exact text the table comment documents.
        Auth.Domain.Constants.LoginFailureReasons.AccountLocked.Should().Be("Account locked");
        Auth.Domain.Constants.LoginFailureReasons.InvalidPassword.Should().Be("Invalid password");
    }

    [Fact]
    public void TheLockedOutMetricStillMatchesTheLiteralTheLoginFlowWrites()
    {
        // Coupled to prose by design, and noted in the table comment. If the
        // vocabulary is ever reworked this metric silently reports zero rather
        // than failing, so it is pinned here.
        ReadDashboardRepository().Should().Contain("N'Account locked'");
    }

    private static bool IsTopFailingIpPredicate(string stats, string line)
    {
        var section = Section(stats, "-- Top failing IP addresses", "-- Outcomes per application");
        return section.Contains(line.Trim(), StringComparison.Ordinal);
    }

    private static string Line(string text, int index)
    {
        var start = text.LastIndexOf('\n', index) + 1;
        var end = text.IndexOf('\n', index);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static string Section(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{from}' must be present");

        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1, $"'{to}' must follow '{from}'");

        return text[start..end];
    }

    private static string MethodBody(string source, string signature, string nextSignature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{signature}' must exist");

        var end = source.IndexOf(nextSignature, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1, $"'{nextSignature}' must follow '{signature}'");

        return source[start..end];
    }

    private static string ReadLoginAttemptRepository() =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "LoginAttemptRepository.cs"));

    private static string ReadDashboardRepository() =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "DashboardStatsRepository.cs"));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
