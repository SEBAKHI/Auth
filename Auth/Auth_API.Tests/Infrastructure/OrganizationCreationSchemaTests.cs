using System.Text.RegularExpressions;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of OI-63's schema batch: two columns on <c>Applications</c>,
/// published BEFORE the API that reads them (every application read selects both).
/// <para>
/// The rules that keep the DACPAC publish a plain <c>ALTER TABLE … ADD</c>:
/// - the last two columns, in this order, right after <c>AllowedScopes</c>: DacFx
///   honours declared order, and a column anywhere else rebuilds the table;
/// - <c>AllowOrganizationCreation</c> is NOT NULL, so it carries a NAMED default
///   (an unnamed one gets a random name a later publish cannot match);
/// - <c>OrganizationCreatorRoleId</c> is NULL with nothing else, and above all no
///   foreign key: a role is hard-deleted, and a key would turn that delete into an
///   error for a role chosen as a creator role. The role is re-checked at use instead.
/// </para>
/// </summary>
public class OrganizationCreationSchemaTests
{
    private static string TableSource() =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                "Applications.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    internal static List<(string Name, string Definition)> DeclaredColumns(string source, string table = "Applications")
    {
        var columns = new List<(string, string)>();
        var start = source.IndexOf($"CREATE TABLE [dbo].[{table}]", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

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
                columns.Add((match.Groups["name"].Value, match.Groups["definition"].Value.Trim()));
            }
        }

        return columns;
    }

    internal static string StripLineComment(string line)
    {
        var comment = line.IndexOf("--", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    /// <summary>
    /// Columns a LATER batch appended after this one, in their order. Only these
    /// may follow it; each later batch pins its own shape in its own guard
    /// (<see cref="BrandingSchemaTests"/>).
    /// </summary>
    private static readonly string[] LaterBatchColumns = ["LogoUrlDark"];

    [Fact]
    public void TheTwoColumns_AreLast_InOrder_AfterTheAllowedScopes()
    {
        var names = DeclaredColumns(TableSource()).Select(c => c.Name).ToList();

        names.Count.Should().BeGreaterThan(3);
        names.Skip(names.IndexOf("AllowedScopes")).Should().Equal(
            ["AllowedScopes", "AllowOrganizationCreation", "OrganizationCreatorRoleId", .. LaterBatchColumns],
            "the batch appends after OI-58's column and only a later batch follows it");
    }

    [Fact]
    public void TheSwitch_IsNotNull_WithANamedDefaultOfOff()
    {
        var column = DeclaredColumns(TableSource()).Single(c => c.Name == "AllowOrganizationCreation");

        column.Definition.Should().MatchRegex(
            @"^BIT\s+NOT\s+NULL\s+CONSTRAINT\s+\[DF_Applications_AllowOrganizationCreation\]\s+DEFAULT\s+\(0\)$");
    }

    [Fact]
    public void TheCreatorRole_IsANullableIdWithoutAForeignKey()
    {
        var column = DeclaredColumns(TableSource()).Single(c => c.Name == "OrganizationCreatorRoleId");
        column.Definition.Should().MatchRegex(@"^UNIQUEIDENTIFIER\s+NULL$");

        var mentions = Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB"), "*.sql", SearchOption.AllDirectories)
            .Sum(file => Regex.Matches(
                string.Join('\n', File.ReadAllLines(file).Select(StripLineComment)),
                @"(?<![\w@])OrganizationCreatorRoleId(?!\w)").Count);

        mentions.Should().Be(1, "the column is declared and named nowhere else: no foreign key, no index, no CHECK");
    }

    [Fact]
    public void Readiness_NamesEachColumnOnItsOwn()
    {
        foreach (var column in new[] { "AllowOrganizationCreation", "OrganizationCreatorRoleId" })
        {
            DatabaseReadinessHealthCheck.SchemaExpectations.Should().ContainSingle(expectation =>
                expectation.Sql.Contains($"COL_LENGTH('dbo.Applications', '{column}') IS NOT NULL")
                && expectation.Name.Contains($"Applications.{column}"));
        }
    }
}
