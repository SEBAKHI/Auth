using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards what the application writes actually persist.
///
/// `CreateAsync` used to omit `[ReauthenticationMaxAgeMinutes]` from its INSERT
/// and knew nothing about the redirect-URI allowlist, so both settings were
/// accepted by the API, echoed back in the 201 response, and silently gone on
/// the next read. The repositories are Dapper + raw SQL and the test project has
/// no database, so the SQL text itself is the unit under test.
/// </summary>
public class ApplicationPersistenceSqlTests
{
    [Fact]
    public void CreateInsert_CoversEveryColumnTheUpdateCanWrite()
    {
        // The invariant, rather than one column: anything the update path can
        // change must be settable at creation, or creating an application with
        // that value quietly stores the column default instead.
        var sql = ReadRepository();

        var inserted = ColumnList(Match(
            sql,
            @"INSERT\s+INTO\s+\[dbo\]\.\[Applications\]\s*\((?<columns>[^)]*)\)",
            "columns"));

        var updateBody = Regex.Matches(
                sql,
                @"UPDATE\s+\[dbo\]\.\[Applications\]\s+SET(?<body>.*?)WHERE",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Groups["body"].Value)
            // The other UPDATE on this table is the soft delete in DeleteAsync.
            .Single(body => body.Contains("[MaxConcurrentSessions]", StringComparison.OrdinalIgnoreCase));

        var updated = Regex.Matches(updateBody, @"\[(?<column>\w+)\]\s*=")
            .Select(match => match.Groups["column"].Value);

        updated.Except(inserted, StringComparer.OrdinalIgnoreCase).Should().BeEmpty(
            "every column UpdateAsync writes must also be in the CreateAsync INSERT; " +
            "a column missing from the INSERT makes the setting a no-op at creation " +
            "and the API still reports the value the caller sent");
    }

    [Fact]
    public void CreateAsync_PersistsTheRedirectUriAllowlistTransactionally()
    {
        var createAsync = MethodBody("public async Task<AppEntity> CreateAsync", "public async Task UpdateAsync");

        createAsync.Should().Contain("BeginTransaction",
            "the application row and its allowlist must land together");
        Regex.IsMatch(createAsync, @"INSERT\s+INTO\s+\[dbo\]\.\[ApplicationRedirectUris\]", RegexOptions.IgnoreCase)
            .Should().BeTrue(
                "redirect URIs supplied at creation must be written, not dropped on the floor");
    }

    [Fact]
    public void UpdateAsync_RewritesTheAllowlistInsideOneTransaction()
    {
        var updateAsync = MethodBody("public async Task UpdateAsync", "public async Task DeleteAsync");

        updateAsync.Should().Contain("BeginTransaction",
            "the allowlist is synced by delete-and-reinsert; a failure between the two " +
            "would leave the application with no redirect URIs and break every " +
            "authorization request for it");
    }

    private static IEnumerable<string> ColumnList(string columns) =>
        Regex.Matches(columns, @"\[(?<column>\w+)\]")
            .Select(match => match.Groups["column"].Value)
            .ToList();

    private static string Match(string input, string pattern, string group)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        match.Success.Should().BeTrue($"the repository must contain a statement matching {pattern}");
        return match.Groups[group].Value;
    }

    /// <summary>Text between two method signatures, in declaration order.</summary>
    private static string MethodBody(string signature, string nextSignature)
    {
        var sql = ReadRepository();
        var start = sql.IndexOf(signature, StringComparison.Ordinal);
        var end = sql.IndexOf(nextSignature, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"'{signature}' must exist in ApplicationRepository");
        end.Should().BeGreaterThan(start, $"'{nextSignature}' must follow '{signature}'");

        return sql[start..end];
    }

    private static string ReadRepository() =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "ApplicationRepository.cs"));

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
