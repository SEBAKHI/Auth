using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the fix for the "Violation of UNIQUE KEY constraint 'UQ_UserRoles'" 500.
///
/// UQ_UserRoles (UserId, RoleId, ApplicationId) and UQ_UserPermissions
/// (UserId, PermissionId, ApplicationId) have no [IsActive] filter, while every read
/// path filters [IsActive] = 1. Soft-deleting a removal therefore leaves an invisible
/// row that the next assignment collides with — re-assigning a previously removed role
/// fails with SQL error 2627.
///
/// The repositories are Dapper + raw SQL and the test project has no database, so the
/// SQL text itself is the unit under test: removal must be a DELETE, and no
/// soft-delete UPDATE may reappear on these two tables.
/// </summary>
public class AssignmentRemovalSqlTests
{
    private static readonly string[] RepositoryFiles =
    [
        "RoleRepository.cs",
        "UserRepository.cs",
        "PermissionRepository.cs"
    ];

    [Theory]
    [InlineData("UserRoles")]
    [InlineData("UserPermissions")]
    public void AssignmentTables_AreNeverSoftDeleted(string table)
    {
        var softDelete = new Regex(
            $@"UPDATE\s+\[dbo\]\.\[{table}\]\s+SET\s+\[IsActive\]\s*=\s*0",
            RegexOptions.IgnoreCase);

        var offenders = RepositoryFiles
            .Where(file => softDelete.IsMatch(ReadRepository(file)))
            .ToList();

        offenders.Should().BeEmpty(
            $"deactivating a [dbo].[{table}] row leaves it behind the UNIQUE constraint, " +
            "so the same assignment can never be recreated; remove it with DELETE instead");
    }

    [Theory]
    [InlineData("UserRepository.cs", "UserRoles")]
    [InlineData("UserRepository.cs", "UserPermissions")]
    public void RemovalIsAHardDelete(string file, string table)
    {
        var hardDelete = new Regex(
            $@"DELETE\s+FROM\s+\[dbo\]\.\[{table}\]",
            RegexOptions.IgnoreCase);

        hardDelete.IsMatch(ReadRepository(file)).Should().BeTrue(
            $"{file} must remove [dbo].[{table}] assignments with a DELETE");
    }

    /// <summary>
    /// An assignment is (user, role or permission, scope), the key UQ_UserRoles and
    /// UQ_UserPermissions enforce. Removing a role from a user's application-scoped
    /// assignment reported success and removed nothing: the existence check before it
    /// ignored the scope, the DELETE pinned it to the platform, and the row count was
    /// discarded. And an unused repository method deleted the pair at every scope at
    /// once, waiting to be reached for as the quick fix.
    /// </summary>
    /// <remarks>
    /// Across every repository, the only DELETEs on these tables are the account purge
    /// and the one scoped removal, which names both scopes and returns its row count.
    /// Deliberate breaks: drop <c>sql += " AND [ApplicationId] IS NULL";</c>, or restore
    /// <c>RoleRepository.RemoveFromUserAsync</c>.
    /// </remarks>
    [Theory]
    [InlineData("UserRoles", "RoleId", "public async Task<bool> RemoveRoleAsync(", "removed")]
    [InlineData("UserPermissions", "PermissionId", "public async Task<bool> RevokePermissionAsync(", "revoked")]
    public void TheOnlyRemovalOfOneAssignment_NamesItsScope_AndReportsWhetherItRemovedOne(
        string table, string idColumn, string method, string countVariable)
    {
        // The WHERE clause up to the end of its statement or of its C# string, whichever
        // comes first: the account purge is one statement among many in one string.
        var delete = new Regex($@"DELETE\s+FROM\s+\[dbo\]\.\[{table}\]\s+WHERE\s+(?<where>[^"";\r\n]*;?)");

        var deletes = Directory
            .GetFiles(Path.Combine(SolutionDirectory(), "Auth.Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => delete.Matches(File.ReadAllText(file))
                .Select(match => (File: Path.GetFileName(file), Where: Regex.Replace(match.Groups["where"].Value, @"\s+", " ").Trim())))
            .ToList();

        deletes.Should().BeEquivalentTo(
            new[]
            {
                ("UserRepository.cs", "[UserId] = @Id;"),
                ("UserRepository.cs", $"[UserId] = @UserId AND [{idColumn}] = @{idColumn}"),
            },
            $"a [dbo].[{table}] row is removed either with its account or by its whole key, scope included");

        var removal = MethodText(ReadRepository("UserRepository.cs"), method);
        removal.Should().Contain("sql += \" AND [ApplicationId] = @ApplicationId\";")
            .And.Contain("sql += \" AND [ApplicationId] IS NULL\";",
                "a null scope is the platform's own assignment, which SQL reaches only with IS NULL");
        removal.Should().Contain($"var {countVariable} = await connection.ExecuteAsync(")
            .And.Contain($"return {countVariable} == 1;",
                "the caller reports and audits a removal only when this DELETE says it removed one");
    }

    private static string MethodText(string source, string signature)
    {
        var normalised = source.Replace("\r\n", "\n");
        var start = normalised.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"'{signature}' must exist; if it was renamed, update this guard rather than deleting it");

        var end = normalised.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return normalised[start..end];
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string ReadRepository(string fileName) =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", fileName));

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
