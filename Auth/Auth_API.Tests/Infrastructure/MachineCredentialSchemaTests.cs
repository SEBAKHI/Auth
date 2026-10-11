using System.Text.RegularExpressions;
using Auth_API.Common.HealthChecks;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The two tables of the machine credentials (OI-128), the only schema change of their deploy,
/// published before the API that reads them. As for TwoFactorBindCodes: every constraint is named
/// (an unnamed DEFAULT gets a per-database name that every compare reads as drift), there is no
/// CHECK (a CHECK's text is what the nine known benign drifts are), and the files are read as text,
/// because CI does not build the database project.
/// </summary>
public class MachineCredentialSchemaTests
{
    public static TheoryData<string> Tables => new() { "ApplicationMachineKeys", "ClientAssertionJtis" };

    [Theory]
    [MemberData(nameof(Tables))]
    public void TheTable_IsBuiltByTheDatabaseProject(string table)
    {
        var project = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "Auth_DB.sqlproj"));

        project.Should().Contain($@"<Build Include=""dbo\Tables\Security\{table}.sql"" />",
            "a table file the project does not list is never published");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryDefault_IsNamed_AndThereIsNoCheck(string table)
    {
        var sql = WithoutComments(Table(table));

        var defaults = Regex.Matches(sql, @"\bDEFAULT\b", RegexOptions.IgnoreCase).Count;
        var named = Regex.Matches(sql, $@"CONSTRAINT\s+\[DF_{table}_\w+\]\s+DEFAULT\b", RegexOptions.IgnoreCase).Count;
        named.Should().Be(defaults);
        Regex.IsMatch(sql, @"\bCHECK\b", RegexOptions.IgnoreCase).Should().BeFalse("no CHECK: its text is what drifts");
        Regex.IsMatch(sql, @"ON\s+DELETE", RegexOptions.IgnoreCase).Should().BeFalse("applications are soft-deleted, never removed");
        sql.Should().MatchRegex($@"CONSTRAINT\s+\[FK_{table}_Applications\]\s+FOREIGN\s+KEY\s+\(\[ApplicationId\]\)\s+REFERENCES\s+\[dbo\]\.\[Applications\]\(\[Id\]\)");
    }

    [Fact]
    public void TheKeyTable_HasTheDesignColumns_InOrder()
    {
        var declared = Regex.Matches(
                WithoutComments(Table("ApplicationMachineKeys")),
                @"^\s*\[(?<name>\w+)\]\s+(?<type>[A-Z0-9]+(?:\(\d+\))?)",
                RegexOptions.Multiline)
            .Select(match => (match.Groups["name"].Value, match.Groups["type"].Value))
            .ToArray();

        declared.Should().Equal(
            ("Id", "UNIQUEIDENTIFIER"),
            ("ApplicationId", "UNIQUEIDENTIFIER"),
            ("Kid", "NVARCHAR(64)"),
            ("Name", "NVARCHAR(200)"),
            ("Algorithm", "NVARCHAR(10)"),
            ("PublicKeyJwk", "NVARCHAR(1000)"),
            ("CreatedAt", "DATETIME2"),
            ("CreatedBy", "UNIQUEIDENTIFIER"),
            ("LastUsedAt", "DATETIME2"),
            ("RevokedAt", "DATETIME2"),
            ("RevokedBy", "UNIQUEIDENTIFIER"),
            ("RevokeReason", "NVARCHAR(200)"));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void ReadinessReportsTheTable_WhenTheApiIsDeployedAheadOfIt(string table)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"OBJECT_ID('dbo.{table}', 'U')")
                           && expectation.Name.Contains(table));
    }

    private static string Table(string table) =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", $"{table}.sql"));

    private static string WithoutComments(string sql) => Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

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
