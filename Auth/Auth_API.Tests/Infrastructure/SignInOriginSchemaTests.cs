using System.Text.RegularExpressions;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the OI-178 schema batch, published BEFORE the API that reads it, so the
/// DACPAC publish stays a plain <c>ALTER TABLE … ADD</c> (as in <see cref="IdpSessionSchemaTests"/>
/// and <see cref="ConfidentialClientSchemaTests"/>): the browser's user agent and device id
/// appended last on AuthorizationCodes, the sign-in's device id appended last on IdpSessions,
/// each NULL with nothing else. The files are read as text, because CI does not build the
/// database project.
/// </summary>
public class SignInOriginSchemaTests
{
    /// <summary>Table, the column the batch follows, and the batch's columns in order with their types.</summary>
    public static TheoryData<string, string, string[], string[]> Batch => new()
    {
        { "AuthorizationCodes", "RequiresClientAuthentication", ["UserAgent", "DeviceId"], ["NVARCHAR(500)", "NVARCHAR(64)"] },
        { "IdpSessions", "LastActivityAt", ["DeviceId"], ["NVARCHAR(64)"] },
    };

    private static string Sql(string table) => File.ReadAllText(Directory.GetFiles(
            Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"), $"{table}.sql", SearchOption.AllDirectories)
        .Should().ContainSingle().Subject);

    private static string WithoutComments(string sql) =>
        string.Join('\n', sql.Split('\n').Select(line => line.IndexOf("--", StringComparison.Ordinal) is var at and >= 0 ? line[..at] : line));

    private static List<string> Columns(string table) => Regex.Matches(
            WithoutComments(Sql(table)), @"^\s*\[(?<name>\w+)\]\s+[A-Z]", RegexOptions.Multiline)
        .Select(match => match.Groups["name"].Value)
        .ToList();

    [Theory]
    [MemberData(nameof(Batch))]
    public void TheColumns_AreAppendedLast_InOrder_NullableWithNothingElse(
        string table, string previousColumn, string[] columns, string[] types)
    {
        var sql = WithoutComments(Sql(table));

        Columns(table).TakeLast(1 + columns.Length).Should().Equal([previousColumn, .. columns],
            $"the batch is appended right after {table}.{previousColumn}, last: a column anywhere else makes the publish rebuild the table");

        for (var i = 0; i < columns.Length; i++)
        {
            sql.Should().MatchRegex($@"\[{columns[i]}\]\s+{Regex.Escape(types[i])}\s+NULL\s*,",
                $"{table}.{columns[i]} is {types[i]} NULL with nothing else: no DEFAULT, no NOT NULL");
            Regex.Matches(sql, $@"(?<![\w@]){columns[i]}(?!\w)").Should().ContainSingle(
                $"the batch adds nothing on {table}.{columns[i]} beyond the column itself: no index, no constraint");
        }
    }

    [Theory]
    [InlineData("AuthorizationCodes", "UserAgent")]
    [InlineData("AuthorizationCodes", "DeviceId")]
    [InlineData("IdpSessions", "DeviceId")]
    public void Readiness_NamesEachMissingColumn_WhenTheApiIsDeployedAheadOfIt(string table, string column)
    {
        // Authorize writes all three and every code or IdP session read selects them: an API
        // deployed ahead of the DACPAC would fail every sign-in, so /ready says which is missing.
        DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(
            expectation => expectation.Sql.Contains($"COL_LENGTH('dbo.{table}', '{column}') IS NOT NULL")
                           && expectation.Name.Contains($"{table}.{column}"));
    }
}
