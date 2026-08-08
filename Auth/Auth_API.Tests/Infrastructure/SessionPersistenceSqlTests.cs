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
