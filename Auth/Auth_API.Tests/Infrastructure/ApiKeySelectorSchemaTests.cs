using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The one schema change of OI-128 PR B: <c>ApiKeys.KeySelector</c> and its unique index. Read as
/// text, because CI does not build the database project. The deploy may show only an added column
/// and a created index: the column comes last (a column added anywhere else rebuilds the table),
/// is nullable with no default (existing keys have no selector), and the index is named and
/// filtered (NULLs are not unique to SQL Server).
/// </summary>
public class ApiKeySelectorSchemaTests
{
    [Fact]
    public void KeySelector_IsTheLastColumn_NullableWithoutADefault()
    {
        var sql = Table();
        var body = sql[(sql.IndexOf('(') + 1)..sql.IndexOf("CONSTRAINT [PK_ApiKeys]", StringComparison.Ordinal)];
        var columns = Regex.Matches(body, @"^\s*\[(\w+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        columns.Should().HaveCountGreaterThan(15, "the scan must see the whole table");
        columns.Last().Should().Be("KeySelector");
        Regex.IsMatch(body, @"\[KeySelector\]\s+NVARCHAR\(32\)\s+NULL,\s*$", RegexOptions.Multiline)
            .Should().BeTrue("NVARCHAR(32) NULL, and no DEFAULT: an unnamed default is drift on every compare");
    }

    [Fact]
    public void KeySelector_HasANamedUniqueFilteredIndex()
    {
        Table().Should().Contain(
            "CREATE UNIQUE NONCLUSTERED INDEX [UX_ApiKeys_KeySelector] ON [dbo].[ApiKeys] ([KeySelector]) WHERE [KeySelector] IS NOT NULL;");
    }

    [Fact]
    public void TheDecorativeColumns_Stay_WithTheirNamedDefaults()
    {
        // D-128-3: nothing reads them any more, and dropping them would make the deploy a table
        // rebuild. They keep their defaults, which the insert now relies on.
        var sql = Table();
        sql.Should().Contain("[RateLimitPerMinute] INT NOT NULL CONSTRAINT [DF_ApiKeys_RateLimitPerMinute] DEFAULT 60");
        sql.Should().Contain("[RateLimitPerDay] INT NOT NULL CONSTRAINT [DF_ApiKeys_RateLimitPerDay] DEFAULT 10000");
        sql.Should().Contain("[AllowedOrigins] NVARCHAR(MAX) NULL");
    }

    private static string Table() =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "ApiKeys.sql"))
            .Replace("\r\n", "\n");

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
