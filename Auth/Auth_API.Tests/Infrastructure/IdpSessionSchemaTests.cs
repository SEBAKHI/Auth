using System.Text.RegularExpressions;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the OI-154 schema batch: four columns, published to the
/// database BEFORE the API that reads and writes them. The rules are the ones that
/// keep the DACPAC publish a plain <c>ALTER TABLE … ADD</c> (as in
/// <see cref="P3SchemaBatchTests"/> and <see cref="ScopeSchemaTests"/>):
/// - appended in place: on IdpSessions right after AuthMethods, in this order; on
///   AuthorizationCodes and RefreshTokens last. DacFx honours declared column
///   order, and a column anywhere else makes SqlPackage rebuild the table;
/// - NULL with nothing else: no DEFAULT (a constraint a later publish must name),
///   no NOT NULL existing rows could not satisfy;
/// - named nowhere else in the file: no index, no CHECK, no out-of-line DEFAULT,
///   and no foreign key: the retention sweep deletes IdpSessions rows, and a
///   dangling id must neither block it nor fail its delete.
/// The files are read as text, because CI does not build the database project.
/// </summary>
public class IdpSessionSchemaTests
{
    /// <summary>Table, the column the batch follows, and the batch's columns in order with their types.</summary>
    public static TheoryData<string, string, string[], string[]> Batch => new()
    {
        { "IdpSessions", "AuthMethods", ["IsPersistent", "LastActivityAt"], ["BIT", "DATETIME2"] },
        { "AuthorizationCodes", "Scope", ["IdpSessionId"], ["UNIQUEIDENTIFIER"] },
        { "RefreshTokens", "Scope", ["IdpSessionId"], ["UNIQUEIDENTIFIER"] },
    };

    [Fact]
    public void TheBatch_IsFourColumnsOnThreeTables()
    {
        Batch.Select(row => (string[])row[2]).Sum(columns => columns.Length).Should().Be(4);
        Batch.Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(Batch))]
    public void TheColumns_AreAppendedLast_InOrder_NullableWithNothingElse(
        string table, string previousColumn, string[] columns, string[] types)
    {
        var source = File.ReadAllText(TableFile(table));
        var declared = DeclaredColumns(source, table);

        declared.Select(c => c.Name).TakeLast(1 + columns.Length).Should().Equal([previousColumn, .. columns],
            $"the batch is appended right after {table}.{previousColumn}, in this order, and nothing follows it yet");

        for (var i = 0; i < columns.Length; i++)
        {
            declared.Single(c => c.Name == columns[i]).Definition.Should().MatchRegex(
                $@"^{Regex.Escape(types[i])}\s+NULL$",
                $"{table}.{columns[i]} is {types[i]} NULL with nothing else: no DEFAULT, no NOT NULL");

            Regex.Matches(StripComments(source), $@"(?<![\w@]){Regex.Escape(columns[i])}(?!\w)").Should().ContainSingle(
                $"the batch adds nothing on {table}.{columns[i]} beyond the column itself: no index, no constraint, no foreign key");
        }
    }

    [Theory]
    [InlineData("IdpSessions", "IsPersistent")]
    [InlineData("IdpSessions", "LastActivityAt")]
    [InlineData("AuthorizationCodes", "IdpSessionId")]
    [InlineData("RefreshTokens", "IdpSessionId")]
    public void Readiness_NamesEachMissingColumn_WhenTheApiIsDeployedAheadOfIt(string table, string column)
    {
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"COL_LENGTH('dbo.{table}', '{column}') IS NOT NULL")
                           && expectation.Name.Contains($"{table}.{column}"));
    }

    [Fact]
    public void Readiness_NamesTheProcedureParameter_WhenThePublishStoppedBeforeTheProcedure()
    {
        // Every sign-in and every rotation calls sp_CreateRefreshToken with
        // @IdpSessionId: the columns alone do not make the database ready.
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains("sys.parameters")
                           && expectation.Sql.Contains("OBJECT_ID('dbo.sp_CreateRefreshToken', 'P')")
                           && expectation.Sql.Contains("[name] = '@IdpSessionId'")
                           && expectation.Name.Contains("sp_CreateRefreshToken.@IdpSessionId"));
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
