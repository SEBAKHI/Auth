using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the user hard-delete purge against schema drift.
///
/// Users is referenced by ~20 non-cascading foreign keys plus loose (FK-less)
/// user references in AuditLogs, NotificationOutbox and RevokedTokens. The
/// purge in <c>UserRepository.HardDeleteAsync</c> must account for every one
/// of them: rows the user owns are deleted, actor references on records that
/// belong to other entities are reattributed, and Organizations ownership is
/// resolved at the application layer before the purge runs. These tests parse
/// the DACPAC schema for the authoritative FK list, so adding a new
/// Users-referencing table without extending the purge fails the build.
///
/// The repositories are Dapper + raw SQL and the test project has no database,
/// so the SQL text itself is the unit under test.
/// </summary>
public class UserHardDeleteSqlTests
{
    /// <summary>
    /// Organizations.OwnerId is intentionally not part of the purge SQL: the
    /// command handler resolves owned organizations first (blocking when other
    /// members depend on one, deleting sole-member ones), and the FK turns any
    /// gap into a 409 instead of an orphan.
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> HandledOutsideThePurge =
    [
        ("Organizations", "OwnerId"),
    ];

    [Fact]
    public void EveryForeignKeyReferencingUsers_IsCoveredByThePurge()
    {
        var purgeSql = HardDeleteSql();
        var uncovered = new List<string>();

        foreach (var (table, column) in ForeignKeysReferencingUsers())
        {
            if (HandledOutsideThePurge.Contains((table, column)))
            {
                continue;
            }

            var deleted = new Regex(
                $@"DELETE\s+FROM\s+\[dbo\]\.\[{table}\][^;]*\[{column}\]\s*=\s*@Id",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var reattributed = new Regex(
                $@"UPDATE\s+\[dbo\]\.\[{table}\]\s+SET\s+\[{column}\]",
                RegexOptions.IgnoreCase);

            if (!deleted.IsMatch(purgeSql) && !reattributed.IsMatch(purgeSql))
            {
                uncovered.Add($"{table}.{column}");
            }
        }

        uncovered.Should().BeEmpty(
            "every foreign key referencing Users must be deleted or reattributed by " +
            "HardDeleteAsync, otherwise the purge dies on SQL error 547 — extend the " +
            "purge SQL (or the handler) for these references");
    }

    [Fact]
    public void OrganizationsOwnership_IsResolvedByTheCommandHandler()
    {
        var handler = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Application", "Features", "Users",
            "HardDeleteUser", "HardDeleteUserCommandHandler.cs"));

        handler.Should().Contain("GetByOwnerAsync",
            "the handler must resolve owned organizations before the purge; " +
            "Organizations.OwnerId is exempted from the purge SQL on that basis");
        handler.Should().Contain("GetMemberCountsAsync",
            "owned organizations with other members must block the purge");
    }

    [Theory]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[AuditLogs\]\s+WHERE\s+\[UserId\]\s*=\s*@Id\s+OR\s+\[PerformedBy\]\s*=\s*@Id",
        "the user's audit trail (rows about them and rows they performed) must be purged")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[NotificationOutbox\]\s+WHERE\s+\[RecipientUserId\]\s*=\s*@Id",
        "queued notifications addressed to the user must be purged")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[RevokedTokens\]\s+WHERE\s+\[RevocationKey\]",
        "denylist entries keyed by the user id must be purged")]
    public void LooseUserReferences_AreCoveredByThePurge(string pattern, string because)
    {
        new Regex(pattern, RegexOptions.IgnoreCase).IsMatch(HardDeleteSql())
            .Should().BeTrue(because);
    }

    [Fact]
    public void UsersRow_IsDeletedLast_AndOnlyWhenStillSoftDeleted()
    {
        var purgeSql = HardDeleteSql();

        var finalDelete = new Regex(
            @"DELETE\s+FROM\s+\[dbo\]\.\[Users\]\s+WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[IsDeleted\]\s*=\s*1",
            RegexOptions.IgnoreCase);
        finalDelete.IsMatch(purgeSql).Should().BeTrue(
            "the account row must only be removed while still flagged deleted — " +
            "the predicate is the last in-database guard against purging a live account");

        var match = finalDelete.Match(purgeSql);
        purgeSql[(match.Index + match.Length)..].Should().NotContain("DELETE FROM",
            "the Users row must be the final delete so child rows never outlive it");
    }

    [Fact]
    public void Purge_VerifiesEligibilityUnderAnUpdateLock()
    {
        var repository = ReadPersistenceFile("UserRepository.cs");

        new Regex(@"WITH\s+\(UPDLOCK,\s*HOLDLOCK\)\s+WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[IsDeleted\]\s*=\s*1",
                RegexOptions.IgnoreCase)
            .IsMatch(repository).Should().BeTrue(
                "HardDeleteAsync must re-verify the soft-deleted flag inside the " +
                "transaction under an update lock, so a concurrent write cannot race " +
                "a live account into the purge");
    }

    /// <summary>
    /// Parses every table definition in the DACPAC project for foreign keys
    /// that reference [dbo].[Users], returning (table, referencing column).
    /// </summary>
    private static IReadOnlyList<(string Table, string Column)> ForeignKeysReferencingUsers()
    {
        var tablesDirectory = Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables");
        var foreignKey = new Regex(
            @"FOREIGN\s+KEY\s*\(\[(?<column>\w+)\]\)\s*REFERENCES\s+\[dbo\]\.\[Users\]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var references = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(tablesDirectory, "*.sql", SearchOption.AllDirectories))
        {
            var table = Path.GetFileNameWithoutExtension(file);
            foreach (System.Text.RegularExpressions.Match match in foreignKey.Matches(File.ReadAllText(file)))
            {
                if (!string.Equals(table, "Users", StringComparison.OrdinalIgnoreCase))
                {
                    references.Add((table, match.Groups["column"].Value));
                }
            }
        }

        references.Should().NotBeEmpty("the schema parse found no Users foreign keys — the pattern is broken");
        return references;
    }

    /// <summary>Extracts the HardDeleteAsync method body from the repository source.</summary>
    private static string HardDeleteSql()
    {
        var repository = ReadPersistenceFile("UserRepository.cs");

        var start = repository.IndexOf("HardDeleteAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "UserRepository must implement HardDeleteAsync");

        var end = repository.IndexOf("transaction.Commit", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "HardDeleteAsync must commit its transaction");

        return repository[start..end];
    }

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
