using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the P3 deploy-1 schema batch: six columns that the X01, X02 and S08 code
/// reads, published to the database BEFORE that code so the API never meets a schema without
/// them.
///
/// Each rule is what keeps the DACPAC publish a plain <c>ALTER TABLE … ADD</c>:
/// - declared right after the table's original columns, contiguous and in the planned order:
///   DacFx honours declared column order, so a column inserted mid-table makes SqlPackage
///   rebuild the whole table (a <c>tmp_ms_xx</c> copy and a DROP) instead of adding it in place.
///   Once deploy 1 has run, the same holds for anything wedged between or ahead of these
///   columns. A later batch appends after them;
/// - NULL and without a DEFAULT: existing rows need no value, no constraint is created whose
///   name a later publish would have to match, and code that predates the batch, whose INSERTs
///   name their columns, keeps working;
/// - named nowhere else in its table file: no index, no out-of-line DEFAULT … FOR, no CHECK, no
///   UNIQUE. None of the readers needs one, and each is a deploy of its own.
///
/// The guard reads the table files themselves, so a reordered or tightened column fails the
/// build instead of the publish.
/// </summary>
public class P3SchemaBatchTests
{
    /// <summary>Table, its last original column, and the batch columns in their planned order.</summary>
    public static TheoryData<string, string, string[]> BatchTables => new()
    {
        { "TwoFactorAuth", "ModifiedAt", ["LastUsedTimeStep", "PendingSecretKey", "PendingSecretCreatedAt"] },
        { "UserSessions", "DeviceHash", ["AuthMethods"] },
        { "IdpSessions", "DeviceInfo", ["AuthMethods"] },
        { "TwoFactorChallenges", "CreatedAt", ["PrimaryMethod"] },
    };

    /// <summary>Table, its last original column, the batch column, and the batch column's type.</summary>
    public static TheoryData<string, string, string, string> BatchColumns => new()
    {
        { "TwoFactorAuth", "ModifiedAt", "LastUsedTimeStep", "BIGINT" },
        { "TwoFactorAuth", "ModifiedAt", "PendingSecretKey", "NVARCHAR(500)" },
        { "TwoFactorAuth", "ModifiedAt", "PendingSecretCreatedAt", "DATETIME2" },
        { "UserSessions", "DeviceHash", "AuthMethods", "INT" },
        { "IdpSessions", "DeviceInfo", "AuthMethods", "INT" },
        { "TwoFactorChallenges", "CreatedAt", "PrimaryMethod", "INT" },
    };

    [Fact]
    public void TheBatch_IsSixColumnsOnFourTables()
    {
        // The theory below proves nothing if its data shrinks; pin the batch's size.
        var rows = BatchColumns.Select(row => ((string)row[0], (string)row[2])).ToList();

        rows.Should().HaveCount(6);
        rows.Select(row => row.Item1).Distinct().Should().HaveCount(4);
        BatchTables.Select(row => (string[])row[2]).Sum(columns => columns.Length).Should().Be(6,
            "the per-table data and the per-column data describe the same six columns");
    }

    [Theory]
    [MemberData(nameof(BatchTables))]
    public void BatchColumns_FollowTheOriginalColumns_ContiguouslyAndInPlannedOrder(
        string table, string lastOriginalColumn, string[] batch)
    {
        var columns = DeclaredColumns(File.ReadAllText(TableFile(table)), table)
            .Select(declared => declared.Name)
            .ToList();
        var lastOriginal = columns.IndexOf(lastOriginalColumn);

        lastOriginal.Should().BeGreaterThanOrEqualTo(0, $"{table} declares {lastOriginalColumn}");
        columns.Skip(lastOriginal + 1).Take(batch.Length).Should().Equal(batch,
            $"the batch columns of {table} sit right after {lastOriginalColumn}, in the order deploy 1 " +
            "created them: anything wedged between or ahead of them makes DacFx rebuild the table");
    }

    [Theory]
    [MemberData(nameof(BatchColumns))]
    public void NewColumns_AreNullableAndAfterTheOriginalColumns(
        string table, string lastOriginalColumn, string column, string type)
    {
        var source = File.ReadAllText(TableFile(table));
        var columns = DeclaredColumns(source, table);

        var lastOriginal = columns.FindIndex(declared => declared.Name == lastOriginalColumn);
        var position = columns.FindIndex(declared => declared.Name == column);

        lastOriginal.Should().BeGreaterThanOrEqualTo(0, $"{table} declares {lastOriginalColumn}");
        position.Should().BeGreaterThan(lastOriginal,
            $"{table}.{column} must be declared after {lastOriginalColumn}: a column in the middle of " +
            "the table makes DacFx rebuild the table instead of adding the column");

        var definition = columns[position].Definition;
        definition.Should().MatchRegex($@"^{Regex.Escape(type)}\s+NULL$",
            $"{table}.{column} is {type} NULL with nothing else: no NOT NULL that existing rows " +
            "could not satisfy, no DEFAULT whose constraint a later publish would have to name");

        // Once in its declaration and nowhere else in the file: an index (with or without a
        // terminating semicolon, bracketed or not), an inline INDEX, a UNIQUE or CHECK constraint
        // or an out-of-line DEFAULT … FOR would each have to name the column a second time.
        Regex.Matches(StripComments(source), $@"(?<![\w@]){Regex.Escape(column)}(?!\w)").Should().ContainSingle(
            $"the batch adds nothing on {table}.{column} beyond the column itself: no index, no constraint");
    }

    private sealed record Column(string Name, string Definition);

    /// <summary>
    /// The column declarations of one <c>CREATE TABLE</c>, in declared order, with comments and
    /// the trailing comma removed. Stops at the first table constraint.
    /// </summary>
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

    /// <summary>The file without its block and line comments, which name columns in prose.</summary>
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
                Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                $"{table}.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle($"one file declares [dbo].[{table}]").Subject;

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
