using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the table behind the code emailed before a first second factor (X02 PR B):
/// the one schema change of its deploy, published before the API that writes it.
/// <para>
/// Every constraint is named, because an unnamed DEFAULT or CHECK gets a generated
/// name that differs per database, and each schema compare then reads drift and plans
/// a drop and re-create. There is no CHECK at all: a CHECK's text is what the nine
/// known benign drifts of this database are. The file is parsed as text, because CI
/// does not build the database project.
/// </para>
/// </summary>
public class TwoFactorBindCodesSchemaTests
{
    private static readonly (string Name, string Type, bool Nullable)[] Columns =
    [
        ("Id", "UNIQUEIDENTIFIER", false),
        ("UserId", "UNIQUEIDENTIFIER", false),
        ("CodeHash", "NVARCHAR(500)", false),
        ("ExpiresAt", "DATETIME2", false),
        ("UsedAt", "DATETIME2", true),
        ("AttemptCount", "INT", false),
        ("IpAddress", "NVARCHAR(45)", true),
        ("CreatedAt", "DATETIME2", false),
    ];

    [Fact]
    public void TheTable_IsBuiltByTheDatabaseProject()
    {
        var project = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "Auth_DB.sqlproj"));

        project.Should().Contain(@"<Build Include=""dbo\Tables\Security\TwoFactorBindCodes.sql"" />",
            "a table file the project does not list is never published");
    }

    [Fact]
    public void TheColumns_AreTheEightOfTheDesign_InOrder()
    {
        var declared = Regex.Matches(
                TableBody(),
                @"^\s*\[(?<name>\w+)\]\s+(?<type>[A-Z0-9]+(?:\(\d+\))?)\s+(?<null>NOT NULL|NULL)\b",
                RegexOptions.Multiline)
            .Select(match => (match.Groups["name"].Value, match.Groups["type"].Value, match.Groups["null"].Value == "NULL"))
            .ToArray();

        declared.Should().Equal(Columns);
    }

    [Fact]
    public void EveryConstraint_IsNamed_AndThereIsNoCheck()
    {
        var sql = WithoutComments(Table());

        var defaults = Regex.Matches(sql, @"\bDEFAULT\b", RegexOptions.IgnoreCase).Count;
        var namedDefaults = Regex.Matches(sql, @"CONSTRAINT\s+\[DF_TwoFactorBindCodes_\w+\]\s+DEFAULT\b", RegexOptions.IgnoreCase).Count;
        defaults.Should().Be(3, "Id, AttemptCount and CreatedAt have defaults");
        namedDefaults.Should().Be(defaults, "an unnamed DEFAULT gets a generated name that drifts per database");

        Regex.IsMatch(sql, @"\bCHECK\b", RegexOptions.IgnoreCase).Should().BeFalse("no CHECK: its text is what drifts");
        Regex.IsMatch(sql, @"\bUNIQUE\b", RegexOptions.IgnoreCase).Should().BeFalse("nothing in the design is unique");

        Regex.Matches(sql, @"PRIMARY\s+KEY", RegexOptions.IgnoreCase).Should().ContainSingle();
        sql.Should().MatchRegex(@"CONSTRAINT\s+\[PK_TwoFactorBindCodes\]\s+PRIMARY\s+KEY\s+CLUSTERED\s+\(\[Id\]\)");

        Regex.Matches(sql, @"FOREIGN\s+KEY", RegexOptions.IgnoreCase).Should().ContainSingle();
        sql.Should().MatchRegex(
            @"CONSTRAINT\s+\[FK_TwoFactorBindCodes_Users\]\s+FOREIGN\s+KEY\s+\(\[UserId\]\)\s+REFERENCES\s+\[dbo\]\.\[Users\]\(\[Id\]\)");
        Regex.IsMatch(sql, @"ON\s+DELETE", RegexOptions.IgnoreCase).Should().BeFalse(
            "no cascade: the user hard-delete purge removes the rows itself, as for every sibling table");
    }

    [Fact]
    public void TheIndexes_AreTheTwoOfTheDesign()
    {
        var indexes = Regex.Matches(
                WithoutComments(Table()),
                @"CREATE\s+(?:UNIQUE\s+)?(?:NON)?CLUSTERED\s+INDEX\s+\[(?<name>\w+)\]\s+ON\s+\[dbo\]\.\[TwoFactorBindCodes\]\s+\((?<keys>[^)]*)\)(?:\s+WHERE\s+(?<filter>[^;]*?))?\s*;",
                RegexOptions.IgnoreCase)
            .Select(match => (
                match.Groups["name"].Value,
                Collapse(match.Groups["keys"].Value),
                Collapse(match.Groups["filter"].Value)))
            .ToArray();

        indexes.Should().Equal(
            ("IX_TwoFactorBindCodes_UserId_CreatedAt", "[UserId], [CreatedAt] DESC", ""),
            ("IX_TwoFactorBindCodes_ExpiresAt", "[ExpiresAt]", "[UsedAt] IS NULL"));
    }

    private static string Table() =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "TwoFactorBindCodes.sql"));

    /// <summary>The column list: from the opening parenthesis to the first table constraint.</summary>
    private static string TableBody()
    {
        var sql = WithoutComments(Table());
        var start = sql.IndexOf('(');
        var end = sql.IndexOf("CONSTRAINT [PK_", StringComparison.Ordinal);
        start.Should().BePositive();
        end.Should().BeGreaterThan(start);
        return sql[(start + 1)..end];
    }

    private static string WithoutComments(string sql) => Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

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
