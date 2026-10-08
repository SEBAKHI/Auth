using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the two statements that end an application's sessions (OI-65). The ids they return are
/// blacklisted one by one, so each statement must return exactly the rows IT ended, and only that
/// application's (and, for one user, only that user's): a missing predicate would blacklist, and
/// sign out, sessions of the platform or of other applications.
///
/// The repository is Dapper over raw SQL and this project has no database, so the SQL text is the
/// unit under test.
/// </summary>
public class ApplicationSessionTerminationSqlTests
{
    public static TheoryData<string> Methods => new()
    {
        "TerminateForApplicationAsync",
        "TerminateForUserAndApplicationAsync",
    };

    [Fact]
    public void Methods_IsNotEmpty() => Methods.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(Methods))]
    public void TheStatement_ReturnsOnlyTheOpenRowsOfThatApplicationThatItEnded(string method)
    {
        var body = MethodBody(method);

        Regex.IsMatch(body, @"UPDATE\s+\[dbo\]\.\[UserSessions\]\s+SET").Should().BeTrue(
            "{0} ends rows of [dbo].[UserSessions]", method);
        body.Should().Contain("OUTPUT inserted.[Id]", "the caller blacklists the ids this statement ended");
        body.Should().Contain("[EndedAt] IS NULL", "a row another call already ended is not reported twice");
        body.Should().Contain("[ApplicationId] = @ApplicationId",
            "platform sessions (NULL) and other applications' sessions must stay up");
        body.Should().Contain("QueryAsync<Guid>(", "the OUTPUT rows are read, not discarded");
        body.Should().NotContain("ExecuteAsync(", "an ExecuteAsync would drop the OUTPUT rows");
    }

    [Fact]
    public void TheOneUserStatement_IsLimitedToThatUser()
    {
        MethodBody("TerminateForUserAndApplicationAsync").Should().Contain("[UserId] = @UserId",
            "removing one user's access must not sign every other user out of the application");
    }

    /// <summary>The source of <paramref name="method"/>, up to the next member's doc comment.</summary>
    private static string MethodBody(string method)
    {
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "UserSessionRepository.cs")).Replace("\r\n", "\n");

        var start = source.IndexOf($"public async Task<IReadOnlyList<Guid>> {method}(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "{0} must exist and return the ids it ended", method);
        var end = source.IndexOf("/// <inheritdoc />", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

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
