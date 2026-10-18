using System.Text.RegularExpressions;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the shape of the OI-171 schema batch, published BEFORE the API that reads it, so the
/// DACPAC publish stays a plain <c>ALTER TABLE … ADD</c> and a <c>CREATE TABLE</c> (as in
/// <see cref="IdpSessionSchemaTests"/>): one new table, a copy of the post-logout list's; a
/// nullable BIT appended last on AuthorizationCodes and RefreshTokens; and the key's purpose
/// appended last on ApplicationMachineKeys, NOT NULL with a named default, and no CHECK (its text
/// is what schema compares report as drift, <see cref="MachineCredentialSchemaTests"/>).
/// The files are read as text, because CI does not build the database project.
/// </summary>
public class ConfidentialClientSchemaTests
{
    private static string Sql(string table) => File.ReadAllText(Directory.GetFiles(
            Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"), $"{table}.sql", SearchOption.AllDirectories)
        .Should().ContainSingle().Subject);

    private static string WithoutComments(string sql) =>
        string.Join('\n', sql.Split('\n').Select(line => line.IndexOf("--", StringComparison.Ordinal) is var at and >= 0 ? line[..at] : line));

    private static List<string> Columns(string table) => Regex.Matches(
            WithoutComments(Sql(table)), @"^\s*\[(?<name>\w+)\]\s+[A-Z]", RegexOptions.Multiline)
        .Select(match => match.Groups["name"].Value)
        .ToList();

    [Fact]
    public void TheServerSideList_IsItsOwnTable_LikeThePostLogoutList()
    {
        var sql = WithoutComments(Sql("ApplicationConfidentialRedirectUris"));

        sql.Should().MatchRegex(@"\[Uri\]\s+NVARCHAR\(500\)\s+COLLATE\s+Latin1_General_100_BIN2\s+NOT\s+NULL",
            "compared byte for byte, as the code matches it");
        sql.Should().MatchRegex(@"CONSTRAINT\s+\[FK_ApplicationConfidentialRedirectUris_Applications\]\s+FOREIGN\s+KEY\s+\(\[ApplicationId\]\)\s+REFERENCES\s+\[dbo\]\.\[Applications\]\(\[Id\]\)\s+ON\s+DELETE\s+CASCADE");
        sql.Should().MatchRegex(@"CONSTRAINT\s+\[UQ_ApplicationConfidentialRedirectUris_ApplicationId_Uri\]\s+UNIQUE\s+\(\[ApplicationId\],\s*\[Uri\]\)");
        Regex.Matches(sql, @"\bDEFAULT\b").Count.Should().Be(
            Regex.Matches(sql, @"CONSTRAINT\s+\[DF_ApplicationConfidentialRedirectUris_\w+\]\s+DEFAULT\b").Count, "every default is named");
        sql.Should().Contain("CREATE NONCLUSTERED INDEX [IX_ApplicationConfidentialRedirectUris_ApplicationId]");

        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "Auth_DB.sqlproj"))
            .Should().Contain(@"<Build Include=""dbo\Tables\Core\ApplicationConfidentialRedirectUris.sql"" />");
    }

    /// <summary>
    /// Columns a LATER batch appended after the flag, in their order. Only these may follow it;
    /// each later batch pins its own shape in its own guard (OI-178: <see cref="SignInOriginSchemaTests"/>).
    /// </summary>
    private static readonly Dictionary<string, string[]> LaterBatchColumns = new()
    {
        ["AuthorizationCodes"] = ["UserAgent", "DeviceId"],
    };

    [Theory]
    [InlineData("AuthorizationCodes")]
    [InlineData("RefreshTokens")]
    public void TheBindingFlag_IsAppendedLast_NullableBit_WithNothingElse(string table)
    {
        var sql = Sql(table);
        var later = LaterBatchColumns.GetValueOrDefault(table, []);

        Columns(table).TakeLast(1 + later.Length).Should().Equal(["RequiresClientAuthentication", .. later],
            "a column anywhere else makes the publish rebuild the table, and only later batches follow it");
        WithoutComments(sql).Should().MatchRegex(@"\[RequiresClientAuthentication\]\s+BIT\s+NULL\s*,");
        Regex.Matches(WithoutComments(sql), @"(?<![\w@])RequiresClientAuthentication(?!\w)").Should().ContainSingle(
            "no index, no default, no constraint");
    }

    [Fact]
    public void ThePurpose_IsAppendedLast_NotNullWithANamedDefaultOfMachine_AndNoCheck()
    {
        var sql = WithoutComments(Sql("ApplicationMachineKeys"));

        Columns("ApplicationMachineKeys").Last().Should().Be("Purpose");
        sql.Should().MatchRegex(@"\[Purpose\]\s+TINYINT\s+NOT\s+NULL\s+CONSTRAINT\s+\[DF_ApplicationMachineKeys_Purpose\]\s+DEFAULT\s+\(1\)",
            "existing keys read 1, machine, and the publish adds the column in place");
        Regex.IsMatch(sql, @"\bCHECK\b", RegexOptions.IgnoreCase).Should().BeFalse();
    }
}
