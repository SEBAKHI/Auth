using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards what a session write actually persists.
///
/// Session creation is wrapped in a try/catch that logs and swallows, so that a
/// bookkeeping failure can never cost someone their login. The cost of that
/// choice is that a broken INSERT is invisible: the sign-in succeeds, no session
/// row exists, and the user's own session list simply does not show the session
/// they are sitting in. Handler tests cannot see it either, because they mock the
/// repository. The SQL text and its parameter object are therefore the unit under
/// test — the same approach ApplicationPersistenceSqlTests takes, and for the
/// same reason: the repositories are Dapper + raw SQL and the test project has no
/// database.
/// </summary>
public class SessionPersistenceSqlTests
{
    [Fact]
    public void CreateInsert_BindsEveryParameterItReferences()
    {
        // The bug this exists for: a parameter named in VALUES but absent from
        // the anonymous object throws "Must declare the scalar variable", which
        // the caller's catch then hides.
        var createAsync = MethodBody(
            "public async Task<UserSession> CreateAsync", "public async Task UpdateAsync");

        var parameters = Regex.Matches(ValuesClause(createAsync), @"@(?<name>\w+)")
            .Select(match => match.Groups["name"].Value)
            .Distinct()
            .ToList();

        parameters.Should().NotBeEmpty("the INSERT must bind parameters");

        var bindings = ParameterObject(createAsync);
        var unbound = parameters
            .Where(name => !Regex.IsMatch(
                bindings,
                $@"(\bsession\.{name}\b|\b{name}\s*=)"))
            .ToList();

        unbound.Should().BeEmpty(
            "every @parameter in the INSERT must be supplied by the anonymous object; " +
            "a missing one throws at runtime and the swallowing catch turns it into a " +
            "silent loss of the session row");
    }

    [Fact]
    public void CreateInsert_ColumnsAndValuesLineUp()
    {
        var createAsync = MethodBody(
            "public async Task<UserSession> CreateAsync", "public async Task UpdateAsync");

        var columns = Regex.Matches(ColumnClause(createAsync), @"\[(?<column>\w+)\]").Count;
        var values = Regex.Matches(ValuesClause(createAsync), @"@\w+").Count;

        values.Should().Be(columns,
            "a column list longer or shorter than its VALUES list is a runtime error " +
            "that only shows up as a missing session");
    }

    [Fact]
    public void CreateInsert_WritesEveryDeviceColumn()
    {
        // The linkage is only as good as what reaches the row. DeviceHash in
        // particular is the join key: without it a session can never be
        // attributed to the browser that started it.
        var createAsync = MethodBody(
            "public async Task<UserSession> CreateAsync", "public async Task UpdateAsync");
        var columns = Regex.Matches(ColumnClause(createAsync), @"\[(?<column>\w+)\]")
            .Select(match => match.Groups["column"].Value)
            .ToList();

        columns.Should().Contain(["DeviceType", "DeviceName", "DeviceId", "DeviceHash", "IpAddress"]);
    }

    [Fact]
    public void SelectProjection_DoesNotAliasOneColumnOntoTwoProperties()
    {
        // [DeviceType] AS [DeviceName] fed one column to two entity properties,
        // so DeviceName was always whatever DeviceType held and DeviceType could
        // not be read at all. Both are real columns now and must map to
        // themselves.
        var repository = ReadRepository();

        Regex.IsMatch(repository, @"\[DeviceType\]\s+AS\s+\[DeviceName\]", RegexOptions.IgnoreCase)
            .Should().BeFalse("DeviceType and DeviceName are different facts and different columns");
    }

    [Fact]
    public void SortableDeviceNameResolvesToTheColumnThatHoldsIt()
    {
        // sortBy=deviceName used to order by [DeviceType], a permanently NULL
        // column, so the sort was accepted and did nothing.
        var repository = ReadRepository();

        repository.Should().Contain(
            "(SortFields.Sessions.DeviceName, [\"[DeviceName]\"])",
            "deviceName must sort by the column that holds the device name");
    }

    [Fact]
    public void EvictionRanksByLastActivity_NotByStartTime()
    {
        // Ordering by [StartedAt] would end the session created first, which for
        // most people is the phone they picked up an hour ago. The session nobody
        // has touched in a week is the one they will not miss, and that is
        // [LastActivityAt]. Getting this wrong evicts sessions that are in use
        // and looks, to their owner, exactly like being hijacked.
        var method = EvictionMethod();

        var ordering = Match(
            method, @"ROW_NUMBER\(\)\s*OVER\s*\((?<order>.*?)\)", "order");

        ordering.Should().Contain("[LastActivityAt] DESC");
        Regex.IsMatch(ordering, @"ORDER\s+BY\s+\[StartedAt\]", RegexOptions.IgnoreCase)
            .Should().BeFalse("start time must not be the primary ranking key");
        ordering.Should().Contain("[Id]",
            "the ranking needs a deterministic tie-break: two sessions opened in " +
            "one request share a LastActivityAt to the tick");
    }

    [Fact]
    public void EvictionCountsOnlyLiveSessions()
    {
        // An ended or expired row still sitting in the table must not occupy a
        // slot — CleanupExpiredAsync has no caller, so expired rows accumulate
        // indefinitely and would otherwise evict live sessions on their behalf.
        var method = EvictionMethod();

        method.Should().Contain("[EndedAt] IS NULL");
        method.Should().Contain("[ExpiresAt] > GETUTCDATE()");
    }

    [Fact]
    public void EvictionIsOneStatementThatReportsWhatItChanged()
    {
        // Read-then-write would let two concurrent sign-ins both decide to end
        // the same session, and the caller would revoke, blacklist and email
        // twice for it. A single UPDATE guarded by [EndedAt] IS NULL settles it
        // on the row lock, and OUTPUT returns only the rows this execution
        // actually changed.
        var method = EvictionMethod();

        Regex.Matches(method, @"\bUPDATE\b", RegexOptions.IgnoreCase).Count
            .Should().Be(1, "the eviction must be a single statement");
        method.Should().Contain("OUTPUT",
            "the caller needs the ended rows to revoke their tokens and name them in the email");
        Regex.IsMatch(method, @"OUTPUT[^;]*\bdeleted\.", RegexOptions.IgnoreCase)
            .Should().BeFalse(
                "OUTPUT must read `inserted` — the post-update image — so the returned " +
                "entities describe the session as ended rather than as it was a moment before");

        // The predicate that makes the statement idempotent under concurrency.
        Regex.IsMatch(
            method,
            @"WHERE\s+\[r\]\.\[rn\]\s*>\s*@KeepNewest\s+AND\s+\[s\]\.\[EndedAt\]\s+IS\s+NULL",
            RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Should().BeTrue("the UPDATE must re-check EndedAt so a lost race changes nothing");
    }

    [Fact]
    public void EvictionOutputsEveryColumnTheSelectProjectionDoes()
    {
        // Two projections of one table drift. The OUTPUT list feeds the same
        // UserSession entity as SELECT, so a column added to one and forgotten in
        // the other silently hands the notification and audit handlers a session
        // with a null DeviceName or a default ExpiresAt — the first names the
        // wrong device in an email, the second blacklists for the wrong window.
        var repository = ReadRepository();

        var selectAliases = ProjectionTargets(repository, "SelectColumns");
        var outputAliases = ProjectionTargets(repository, "OutputInsertedColumns");

        // Two empty lists would compare equal and prove nothing.
        selectAliases.Should().Contain(
            ["Id", "UserId", "DeviceName", "ExpiresAt", "LastActivityAt", "TerminationReason"]);

        outputAliases.Should().BeEquivalentTo(selectAliases,
            "the OUTPUT projection and the SELECT projection must map the same columns");
    }

    [Fact]
    public void EvictionRefusesANonPositiveLimit()
    {
        // 0 is the "unlimited" sentinel and a negative rank window would match
        // every row, so an unguarded statement would turn a misconfiguration
        // into a platform-wide sign-out.
        var method = EvictionMethod();

        Regex.IsMatch(method, @"if\s*\(\s*keepNewest\s*<=\s*0\s*\)")
            .Should().BeTrue("TerminateBeyondLimitAsync must return early for a non-positive limit");
    }

    /// <summary>
    /// The property names each projection maps onto: an explicit alias where one
    /// is given, otherwise the column's own name.
    /// </summary>
    private static List<string> ProjectionTargets(string source, string constantName)
    {
        var block = Match(
            source, $@"{constantName}\s*=\s*@""(?<body>[^""]*)""", "body");

        return Regex.Matches(block, @"AS\s+\[(?<alias>\w+)\]|\[(?<column>\w+)\](?!\s*(AS|\.))")
            .Select(match => match.Groups["alias"].Success
                ? match.Groups["alias"].Value
                : match.Groups["column"].Value)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string EvictionMethod() => MethodBody(
        "public async Task<IReadOnlyList<UserSession>> TerminateBeyondLimitAsync",
        "public async Task TerminateAllForUserAsync");

    private static string ColumnClause(string method) =>
        Match(method, @"INSERT\s+INTO\s+\[dbo\]\.\[UserSessions\]\s*\((?<columns>[^)]*)\)", "columns");

    private static string ValuesClause(string method) =>
        Match(method, @"VALUES\s*\((?<values>[^)]*)\)", "values");

    /// <summary>The anonymous object literal Dapper binds the parameters from.</summary>
    private static string ParameterObject(string method)
    {
        var start = method.IndexOf("new\r\n", StringComparison.Ordinal);
        if (start < 0)
        {
            start = method.IndexOf("new\n", StringComparison.Ordinal);
        }

        start.Should().BeGreaterThanOrEqualTo(0, "CreateAsync must build an anonymous parameter object");
        return method[start..];
    }

    private static string Match(string input, string pattern, string group)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        match.Success.Should().BeTrue($"the repository must contain a statement matching {pattern}");
        return match.Groups[group].Value;
    }

    /// <summary>Text between two method signatures, in declaration order.</summary>
    private static string MethodBody(string signature, string nextSignature)
    {
        var source = ReadRepository();
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        var end = source.IndexOf(nextSignature, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"'{signature}' must exist in UserSessionRepository");
        end.Should().BeGreaterThan(start, $"'{nextSignature}' must follow '{signature}'");

        return source[start..end];
    }

    private static string ReadRepository() =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "UserSessionRepository.cs"));

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
