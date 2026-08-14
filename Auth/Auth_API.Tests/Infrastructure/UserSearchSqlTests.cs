using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards what a user search matches, and guards it staying in one place.
///
/// The predicate was hand-copied into seven queries across five repositories,
/// and every copy checked only Email, FirstName and LastName. Searching a
/// person by the name shown on screen found nobody: "Le Ga" is FirstName "Le"
/// and LastName "Ga", so it matched neither column, while FullName — a
/// persisted computed column holding exactly that string — was never read.
/// Username was never read either.
///
/// The repositories are Dapper + raw SQL and the test project has no database,
/// so the SQL text is the unit under test, as in <see cref="UserHardDeleteSqlTests"/>.
/// </summary>
public class UserSearchSqlTests
{
    private static readonly string[] SearchedColumns =
        ["Email", "Username", "FullName", "FirstName", "LastName"];

    [Theory]
    [InlineData("Email", "the address is what most administrators type first")]
    [InlineData("Username", "the sign-in identifier is a name a person may know someone by")]
    [InlineData("FullName", "\"First Last\" as displayed matches no single name column — this is the one that was missing")]
    [InlineData("FirstName", "a given name alone must still match")]
    [InlineData("LastName", "a family name alone must still match")]
    public void ThePredicate_SearchesEveryNameAndIdentifierColumn(string column, string because)
    {
        UserSearchSql().Should().Contain($"[{column}] LIKE", because);
    }

    [Fact]
    public void ThePredicate_IsInertWhenNothingWasTyped()
    {
        UserSearchSql().Should().Contain("IS NULL OR",
            "a null pattern must disable filtering rather than match nothing");
    }

    [Fact]
    public void ThePredicate_QualifiesEveryColumnWithTheCallersAlias()
    {
        // Joined queries alias Users; an unqualified column would either fail to
        // compile in SQL or, worse, silently bind to another table's column.
        var aliased = Matches("u");

        foreach (var column in SearchedColumns)
        {
            aliased.Should().Contain($"u.[{column}] LIKE",
                $"{column} must be read from the aliased Users row");
        }
    }

    [Fact]
    public void ThePredicate_OmitsTheDotWhenThereIsNoAlias()
    {
        var unaliased = Matches("");

        unaliased.Should().Contain("[Email] LIKE");
        unaliased.Should().NotContain(".[Email] LIKE");
    }

    [Theory]
    [InlineData("UserRepository.cs")]
    [InlineData("ApplicationRepository.cs")]
    [InlineData("OrganizationRepository.cs")]
    [InlineData("RoleRepository.cs")]
    [InlineData("PermissionRepository.cs")]
    public void NoRepository_HandWritesItsOwnUserSearch(string fileName)
    {
        var repository = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", fileName));

        // Any LIKE against a user identity column outside the shared helper is a
        // copy that will drift — the seven that existed all drifted the same way,
        // by never being extended when FullName and Username were added.
        var handWritten = new Regex(
            @"\[(Email|Username|FullName|FirstName|LastName)\]\s+LIKE",
            RegexOptions.IgnoreCase);

        handWritten.IsMatch(repository).Should().BeFalse(
            $"{fileName} must build its user search from UserSearchSql.Matches(), " +
            "so every list in the console agrees on what a search finds");
    }

    /// <summary>
    /// Calls the internal helper the repositories use, so the assertions read
    /// the emitted SQL rather than a copy of it.
    /// </summary>
    private static string Matches(string alias)
    {
        var type = typeof(Auth.Infrastructure.Persistence.SqlConnectionFactory).Assembly
            .GetType("Auth.Infrastructure.Persistence.UserSearchSql");

        type.Should().NotBeNull("UserSearchSql must exist as the single definition");

        var method = type!.GetMethod("Matches");
        method.Should().NotBeNull("UserSearchSql.Matches is the entry point");

        return (string)method!.Invoke(null, [alias, "@SearchPattern"])!;
    }

    private static string UserSearchSql() => Matches("u");

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
