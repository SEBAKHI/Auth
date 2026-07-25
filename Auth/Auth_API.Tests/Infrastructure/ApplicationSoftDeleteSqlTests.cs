using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the application soft-delete conversion (the "SQL 547 on
/// DELETE /Applications" 500).
///
/// Applications is referenced by 17 foreign keys, 15 of them NO ACTION, so a
/// physical DELETE is impossible for any application with history — revoked
/// API keys alone block it. Deletion is a soft delete (IsDeleted = 1,
/// IsActive = 0) inside one transaction that also revokes the application's
/// API and webhook keys, and every operational read filters IsDeleted = 0.
///
/// The repositories are Dapper + raw SQL and the test project has no database,
/// so the SQL text itself is the unit under test.
/// </summary>
public class ApplicationSoftDeleteSqlTests
{
    [Fact]
    public void Applications_AreNeverPhysicallyDeleted()
    {
        var hardDelete = new Regex(
            @"DELETE\s+FROM\s+\[dbo\]\.\[Applications\]",
            RegexOptions.IgnoreCase);

        hardDelete.IsMatch(ReadPersistenceFile("ApplicationRepository.cs")).Should().BeFalse(
            "application deletion must be a soft delete; a physical DELETE dies on the " +
            "15 non-cascading foreign keys (SQL error 547) the moment any child row exists");
    }

    [Fact]
    public void DeleteAsync_SoftDeletesAndRevokesOwnedCredentials()
    {
        var sql = ReadPersistenceFile("ApplicationRepository.cs");

        Regex(@"UPDATE\s+\[dbo\]\.\[Applications\]\s+SET\s+\[IsDeleted\]\s*=\s*1")
            .IsMatch(sql).Should().BeTrue("DeleteAsync must set IsDeleted = 1");
        Regex(@"UPDATE\s+\[dbo\]\.\[ApiKeys\]\s+SET\s+\[RevokedAt\]")
            .IsMatch(sql).Should().BeTrue("DeleteAsync must revoke the application's API keys");
        Regex(@"UPDATE\s+\[dbo\]\.\[WebhookKeys\]\s+SET\s+\[RevokedAt\]")
            .IsMatch(sql).Should().BeTrue("DeleteAsync must revoke the application's webhook keys");
    }

    [Theory]
    [InlineData(@"WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[IsDeleted\]\s*=\s*0", "GetByIdAsync")]
    [InlineData(@"WHERE\s+\[Code\]\s*=\s*@Code\s+AND\s+\[IsDeleted\]\s*=\s*0", "GetByCodeAsync")]
    [InlineData(@"WHERE\s+\[IsDeleted\]\s*=\s*0\s+ORDER\s+BY\s+\[Code\]", "GetAllAsync")]
    [InlineData(@"WHERE\s+\[IsActive\]\s*=\s*1\s+AND\s+\[IsDeleted\]\s*=\s*0", "GetActiveAsync")]
    [InlineData(@"WHERE\s+\[IsDeleted\]\s*=\s*0""", "GetPagedAsync (base where-clause)")]
    public void OperationalReads_ExcludeSoftDeletedApplications(string pattern, string queryName)
    {
        Regex(pattern).IsMatch(ReadPersistenceFile("ApplicationRepository.cs")).Should().BeTrue(
            $"{queryName} must exclude soft-deleted applications");
    }

    [Fact]
    public void ExistsByCode_KeepsDeletedCodesReserved()
    {
        // ExistsByCodeAsync is the one read that must NOT filter IsDeleted:
        // a deleted application's code stays reserved forever.
        var sql = ReadPersistenceFile("ApplicationRepository.cs");
        Regex(@"SELECT\s+COUNT\(1\)\s+FROM\s+\[dbo\]\.\[Applications\]\s+WHERE\s+\[Code\]\s*=\s*@Code""")
            .IsMatch(sql).Should().BeTrue(
                "ExistsByCodeAsync must count soft-deleted rows too, so deleted codes stay reserved");
    }

    [Theory]
    [InlineData("ApiKeyRepository.cs")]
    [InlineData("WebhookKeyRepository.cs")]
    public void KeyValidation_RequiresLiveParentApplication(string file)
    {
        var sql = ReadPersistenceFile(file);

        Regex(@"INNER\s+JOIN\s+\[dbo\]\.\[Applications\]\s+a\s+ON\s+a\.\[Id\]\s*=\s*k\.\[ApplicationId\]")
            .IsMatch(sql).Should().BeTrue($"{file} key lookups must join the owning application");
        Regex(@"a\.\[IsActive\]\s*=\s*1\s+AND\s+a\.\[IsDeleted\]\s*=\s*0")
            .IsMatch(sql).Should().BeTrue(
                $"{file} must only validate keys of active, non-deleted applications — " +
                "keys of an inactive or soft-deleted application must stop authenticating");
    }

    private static Regex Regex(string pattern) => new(pattern, RegexOptions.IgnoreCase);

    private static string ReadPersistenceFile(string fileName) =>
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
