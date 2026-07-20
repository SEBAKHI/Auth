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
    [InlineData("RoleRepository.cs", "UserRoles")]
    [InlineData("UserRepository.cs", "UserRoles")]
    [InlineData("UserRepository.cs", "UserPermissions")]
    [InlineData("PermissionRepository.cs", "UserPermissions")]
    public void RemovalIsAHardDelete(string file, string table)
    {
        var hardDelete = new Regex(
            $@"DELETE\s+FROM\s+\[dbo\]\.\[{table}\]",
            RegexOptions.IgnoreCase);

        hardDelete.IsMatch(ReadRepository(file)).Should().BeTrue(
            $"{file} must remove [dbo].[{table}] assignments with a DELETE");
    }

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
