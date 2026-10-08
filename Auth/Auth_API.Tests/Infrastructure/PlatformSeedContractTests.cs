using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the platform's global RBAC scope in the seed.
///
/// The Applications table holds external client applications only; platform
/// RBAC lives at the global scope (ApplicationId = NULL). A platform
/// application row (Id 00000000-0000-0000-0000-000000000001, code 'auth') was
/// once seeded and has been retired, so the post-deployment script must never
/// seed it again, nor scope any role or permission to it.
/// </summary>
public class PlatformSeedContractTests
{
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
    public void PlatformSettingsPermissions_HangOffTheGlobalWildcard()
    {
        // These rows historically carried the literal system GUID twice: once as
        // ApplicationId (5th value) and once as CreatedBy (last value). Only the
        // first was retired; CreatedBy must keep the system user. Their parent is
        // the global "*": the auth:* hierarchy they once hung from is not seeded.
        var script = ReadPostDeployment();

        foreach (var code in new[] { "platform-settings:manage", "organizations:read", "organizations:manage" })
        {
            var row = Regex($@"N'{Regex_(code)}',[^;]*?;").Match(script);
            row.Success.Should().BeTrue($"the '{code}' permission seed must exist");
            row.Value.Should().Contain("NULL, N'20000000-0000-0000-0000-000000000001'",
                $"'{code}' must be seeded at the global scope (ApplicationId = NULL) under \"*\"");
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

    [Theory]
    [InlineData("01_DefaultApplications.sql")]
    [InlineData("02_DefaultRoles.sql")]
    [InlineData("03_DefaultPermissions.sql")]
    [InlineData("08_AdditionalPermissions.sql")]
    public void DeadSeedCopies_StayDeleted(string fileName)
    {
        File.Exists(SeedPath(fileName)).Should().BeFalse(
            "a seed file the post-deploy never includes still reads as part of the seed, and this " +
            "one would recreate the retired platform application or scope rows to it if run by hand");
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
