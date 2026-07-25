using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the retirement of the seeded platform application
/// (Id 00000000-0000-0000-0000-000000000001, code 'auth').
///
/// The Applications table holds external client applications only; platform
/// RBAC lives at the global scope (ApplicationId = NULL). These tests pin the
/// deployment contract: the post-deployment script must never re-seed the
/// platform application, and the retire migration must run before the seed
/// steps — otherwise the re-scoped role guards would re-insert hardcoded
/// primary keys on existing databases and fail the publish.
/// </summary>
public class PlatformSeedContractTests
{
    private const string RetireScriptInclude = "2026-07-26_RetirePlatformApplication.sql";

    [Fact]
    public void PostDeployment_NeverSeedsApplications()
    {
        var script = ReadPostDeployment();

        Regex(@"INSERT\s+INTO\s+\[dbo\]\.\[Applications\]").IsMatch(script).Should().BeFalse(
            "the platform application row is retired; Applications holds external client apps only");
        script.Should().NotContain("@AuthAppId",
            "no seed row may be scoped to the retired platform application");
    }

    [Fact]
    public void PostDeployment_RunsRetireMigrationBeforeSeedSteps()
    {
        var script = ReadPostDeployment();

        var includeIndex = script.IndexOf(RetireScriptInclude, StringComparison.OrdinalIgnoreCase);
        includeIndex.Should().BeGreaterThan(-1,
            "the retire migration must be part of every publish");

        var step2Index = script.IndexOf("Step 2:", StringComparison.OrdinalIgnoreCase);
        step2Index.Should().BeGreaterThan(-1);
        includeIndex.Should().BeLessThan(step2Index,
            "the retire migration must re-scope existing rows BEFORE the role seed guards run, " +
            "or the guards re-insert hardcoded primary keys on existing databases");
    }

    [Fact]
    public void RoleSeeds_AreGlobalScope()
    {
        var script = ReadPostDeployment();

        foreach (var code in new[] { "admin", "user-manager", "auditor" })
        {
            Regex($@"\[Code\]\s*=\s*N'{code}'\s+AND\s+\[ApplicationId\]\s+IS\s+NULL")
                .IsMatch(script).Should().BeTrue(
                    $"the '{code}' role seed guard must match the global (NULL) scope");
        }
    }

    [Fact]
    public void Step8PlatformPermissions_KeepCreatedByButNotApplicationId()
    {
        // The Step 8 rows historically carried the literal system GUID twice:
        // once as ApplicationId (5th value) and once as CreatedBy (last value).
        // Only the first was retired; CreatedBy must keep the system user.
        var script = ReadPostDeployment();

        foreach (var code in new[] { "platform-settings:manage", "organizations:read", "organizations:manage" })
        {
            var row = Regex($@"N'{Regex_(code)}',[^;]*?;").Match(script);
            row.Success.Should().BeTrue($"the '{code}' permission seed must exist");
            row.Value.Should().Contain("NULL, N'20000000-0000-0000-0000-000000000002'",
                $"'{code}' must be seeded at the global scope (ApplicationId = NULL)");
            row.Value.Should().Contain("GETUTCDATE(), '00000000-0000-0000-0000-000000000001')",
                $"'{code}' must keep the seeded system user as CreatedBy");
        }
    }

    [Fact]
    public void NotificationPermissionSeeds_AreGlobalScope()
    {
        var script = File.ReadAllText(SeedPath("13_NotificationPermissions.sql"));

        script.Should().NotContain("@AuthAppId",
            "notification permissions are platform (global-scope) permissions");
    }

    [Fact]
    public void RetireMigration_IsSqlcmdSafeAndTargetsTheRetiredApplication()
    {
        var script = File.ReadAllText(Path.Combine(
            DbScriptsDirectory(), "Upgrades", RetireScriptInclude));

        script.Should().Contain("SET QUOTED_IDENTIFIER ON",
            "filtered indexes reject DML without QUOTED_IDENTIFIER ON when run via sqlcmd (Msg 1934)");
        Regex(@"UPDATE\s+\[dbo\]\.\[Permissions\][\s\S]*?\[ApplicationId\]\s*=\s*@AuthAppId")
            .IsMatch(script).Should().BeTrue(
                "the migration must blanket re-scope permissions by ApplicationId (historic rows " +
                "from the retired 08 seed have unknown Ids)");
        Regex(@"DELETE\s+FROM\s+\[dbo\]\.\[Applications\]\s+WHERE\s+\[Id\]\s*=\s*@AuthAppId")
            .IsMatch(script).Should().BeTrue(
                "the migration must remove the retired platform application row");
    }

    [Fact]
    public void DeadApplicationSeedCopy_StaysDeleted()
    {
        File.Exists(SeedPath("01_DefaultApplications.sql")).Should().BeFalse(
            "the dead seed copy would re-create the retired platform application if run by hand");
    }

    private static Regex Regex(string pattern) => new(pattern, RegexOptions.IgnoreCase);

    private static string Regex_(string literal) => System.Text.RegularExpressions.Regex.Escape(literal);

    private static string ReadPostDeployment() =>
        File.ReadAllText(Path.Combine(
            DbScriptsDirectory(), "..", "PostDeployment", "Script.PostDeployment.sql"));

    private static string SeedPath(string fileName) =>
        Path.Combine(DbScriptsDirectory(), "SeedData", fileName);

    private static string DbScriptsDirectory() =>
        Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Scripts");

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
