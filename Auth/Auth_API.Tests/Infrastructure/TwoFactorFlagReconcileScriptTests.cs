using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The manual script that reconciles <c>Users.IsTwoFactorEnabled</c> with the
/// factor row. It changes which accounts are asked for a second factor, so the
/// owner must see — and keep — every account it changes BEFORE it changes it, and
/// it must never run by itself on a publish.
/// </summary>
public class TwoFactorFlagReconcileScriptTests
{
    private const string ScriptName = "2026-10-02_TwoFactorFlagReconcile.sql";

    private static string ScriptPath() => Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "Upgrades", ScriptName);

    private static string StripComments(string sql) =>
        Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

    [Fact]
    public void Script_ListsIdAndEmail_BeforeItsUpdate()
    {
        var sql = StripComments(File.ReadAllText(ScriptPath()));

        var listing = Regex.Match(sql, @"SELECT\s+\[Id\],\s*\[Email\],\s*\[FlagBefore\],\s*\[FlagAfter\]\s+FROM\s+@Changes", RegexOptions.IgnoreCase);
        var update = Regex.Match(sql, @"UPDATE\s+u\s+SET", RegexOptions.IgnoreCase);

        listing.Success.Should().BeTrue("the script must print Id, Email and both values of every account it changes");
        update.Success.Should().BeTrue("the script must update the flag");
        listing.Index.Should().BeLessThan(update.Index,
            "the list is the notification and undo list: it must be printed before anything changes");

        // The update changes exactly the accounts listed.
        Regex.IsMatch(sql, @"UPDATE\s+u\s+SET[\s\S]*?FROM\s+\[dbo\]\.\[Users\]\s+u\s+INNER\s+JOIN\s+@Changes\s+c\s+ON\s+c\.\[Id\]\s*=\s*u\.\[Id\]",
                RegexOptions.IgnoreCase)
            .Should().BeTrue("only the listed accounts may change");
    }

    [Fact]
    public void Script_SetsTheFlagToWhetherAnEnabledFactorExists_AndIsIdempotent()
    {
        var sql = StripComments(File.ReadAllText(ScriptPath()));

        Regex.IsMatch(sql,
                @"EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+\[dbo\]\.\[TwoFactorAuth\]\s+t\s+WHERE\s+t\.\[UserId\]\s*=\s*u\.\[Id\]\s+AND\s+t\.\[IsEnabled\]\s*=\s*1\s*\)",
                RegexOptions.IgnoreCase)
            .Should().BeTrue("the factor row is what verification reads, so the flag follows it");
        Regex.IsMatch(sql, @"WHERE\s+u\.\[IsTwoFactorEnabled\]\s*<>\s*f\.\[HasEnabledFactor\]", RegexOptions.IgnoreCase)
            .Should().BeTrue("only accounts that disagree are touched, so a second run changes nothing");
        sql.Should().Contain("BEGIN TRANSACTION").And.Contain("COMMIT TRANSACTION");
    }

    [Fact]
    public void Script_IsANoneItem_NeverInThePostDeploymentChain()
    {
        var root = ApiSourceScan.SolutionDirectory();

        var project = File.ReadAllText(Path.Combine(root, "Auth_DB", "Auth_DB.sqlproj"));
        project.Should().Contain($"<None Include=\"dbo\\Scripts\\Upgrades\\{ScriptName}\" />",
            "a manual script ships with the project but is never built into the DACPAC");
        project.Should().NotContain($"<Build Include=\"dbo\\Scripts\\Upgrades\\{ScriptName}\"");
        project.Should().NotContain($"<PostDeploy Include=\"dbo\\Scripts\\Upgrades\\{ScriptName}\"");

        var postDeployment = Directory.EnumerateFiles(Path.Combine(root, "Auth_DB"), "*.sql", SearchOption.AllDirectories)
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"^\s*:r\s", RegexOptions.Multiline))
            .ToList();
        postDeployment.Should().NotBeEmpty("the scan must find the post-deployment chain it checks");

        foreach (var file in postDeployment)
        {
            File.ReadAllText(file).Should().NotContain("TwoFactorFlagReconcile",
                $"{Path.GetFileName(file)} would run the reconcile on every publish");
        }
    }
}
