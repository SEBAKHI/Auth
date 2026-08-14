namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the alias that carries a user's display name out of the database.
/// <para>
/// The Users table has no DisplayName column: it has FullName, a computed column
/// over FirstName and LastName. Dapper binds by column name and the repository's
/// row type has a DisplayName property and no FullName one, so a SELECT that
/// forgets the alias drops the column silently — no exception, no warning, just a
/// null display name on every screen that reads through these procedures. That is
/// exactly how the profile page came to render a blank name while the users list,
/// whose inline query does alias it, rendered the right one.
/// </para>
/// <para>
/// This is a source-contract test rather than an HTTP one because the test project
/// has no host harness; the precedent for reading repository source off disk is
/// <c>SystemSettingsApplyCoverageTests</c>.
/// </para>
/// </summary>
public class UserReadProjectionTests
{
    [Theory]
    [InlineData("sp_GetUserById.sql")]
    [InlineData("sp_GetUserByEmail.sql")]
    public void UserReadProcedure_AliasesFullNameAsDisplayName(string procedure)
    {
        var path = Path.Combine(
            SolutionDirectory(), "Auth_DB", "dbo", "StoredProcedures", "Users", procedure);

        File.Exists(path).Should().BeTrue($"{procedure} must exist at {path}");

        var sql = File.ReadAllText(path);

        sql.Should().Contain(
            "[FullName] AS [DisplayName]",
            $"{procedure} feeds a reader that binds by column name and has no FullName property");

        // The unaliased form is the regression: it compiles, it runs, and it
        // returns a row — with the name missing.
        sql.Should().NotContain(
            "[FullName],",
            $"an unaliased [FullName] in {procedure} would be dropped without error");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }
}
