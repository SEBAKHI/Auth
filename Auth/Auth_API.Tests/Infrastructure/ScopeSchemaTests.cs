using System.Text.RegularExpressions;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the OI-58 schema batch: three columns the scope code reads,
/// published to the database BEFORE that code (the API's first application read
/// selects one of them).
///
/// The rules are the ones that keep the DACPAC publish a plain
/// <c>ALTER TABLE … ADD</c> (the same as <see cref="P3SchemaBatchTests"/>):
/// - the LAST column of its table: DacFx honours declared column order, and a
///   column anywhere else makes SqlPackage rebuild the table (a <c>tmp_ms_xx</c>
///   copy and a DROP). A later batch appends after it;
/// - NVARCHAR(200) NULL and nothing more: existing rows need no value, no DEFAULT
///   creates a constraint a later publish must name, and the previous build,
///   whose INSERTs name their columns, keeps working;
/// - named nowhere else in its file: no index, no CHECK, no out-of-line DEFAULT.
/// </summary>
public class ScopeSchemaTests
{
    /// <summary>Table, its column before the batch, and the batch column.</summary>
    public static TheoryData<string, string, string> BatchColumns => new()
    {
        { "Applications", "AccessMode", "AllowedScopes" },
        { "AuthorizationCodes", "IssuedSessionId", "Scope" },
        { "RefreshTokens", "ReasonRevoked", "Scope" },
    };

    [Fact]
    public void TheBatch_IsThreeColumnsOnThreeTables()
    {
        var rows = BatchColumns.Select(row => (string)row[0]).ToList();

        rows.Should().HaveCount(3);
        rows.Distinct().Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(BatchColumns))]
    public void TheColumn_IsLast_Nullable_WithoutDefault_AndNamedNowhereElse(
        string table, string previousColumn, string column)
    {
        var source = File.ReadAllText(TableFile(table));
        var columns = DeclaredColumns(source, table);

        columns.Select(c => c.Name).TakeLast(2).Should().Equal([previousColumn, column],
            $"{table}.{column} is appended right after {previousColumn} and nothing follows it: " +
            "anything wedged elsewhere makes DacFx rebuild the table");

        columns[^1].Definition.Should().MatchRegex(@"^NVARCHAR\(200\)\s+NULL$",
            $"{table}.{column} is NVARCHAR(200) NULL with nothing else: no NOT NULL that existing " +
            "rows could not satisfy, no DEFAULT whose constraint a later publish would have to name");

        Regex.Matches(StripComments(source), $@"(?<![\w@]){Regex.Escape(column)}(?!\w)").Should().ContainSingle(
            $"the batch adds nothing on {table}.{column} beyond the column itself: no index, no constraint");
    }

    private sealed record Column(string Name, string Definition);

    private static List<Column> DeclaredColumns(string source, string table)
    {
        var start = source.IndexOf($"CREATE TABLE [dbo].[{table}]", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the file declares [dbo].[{table}]");

        var columns = new List<Column>();
        foreach (var rawLine in source[start..].Split('\n').Skip(1))
        {
            var line = StripLineComment(rawLine).Trim();
            if (line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) || line.StartsWith(')'))
            {
                break;
            }

            var match = Regex.Match(line, @"^\[(?<name>\w+)\]\s+(?<definition>.+?),?$");
            if (match.Success)
            {
                columns.Add(new Column(match.Groups["name"].Value, match.Groups["definition"].Value.Trim()));
            }
        }

        columns.Should().NotBeEmpty($"[dbo].[{table}] declares columns");
        return columns;
    }

    private static string StripComments(string source) =>
        string.Join('\n', Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline)
            .Split('\n')
            .Select(StripLineComment));

    private static string StripLineComment(string line)
    {
        var comment = line.IndexOf("--", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private static string TableFile(string table) =>
        Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                $"{table}.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle($"one file declares [dbo].[{table}]").Subject;
}
