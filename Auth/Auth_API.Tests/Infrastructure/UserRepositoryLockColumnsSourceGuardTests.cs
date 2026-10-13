using System.Text;
using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// X12, LK1.4: <c>Users.Status</c>, <c>FailedLoginAttempts</c> and <c>LockoutEndUtc</c>
/// are written by the lock statements alone (and by the one insert). A whole-row
/// update built from an entity read minutes earlier used to write a stale lock back:
/// a profile edit undid a burst's lock, an avatar adopted at sign-in lifted one.
/// </summary>
/// <remarks>
/// <para>
/// Read from the source, over every production C# file. Each <c>UPDATE</c> or
/// <c>MERGE</c> of the Users table, in any case and with or without brackets or the
/// <c>dbo</c> schema, is read as the SQL its C# expression builds: the rest of its
/// string, every literal and <c>const string</c> concatenated after it (a constant
/// that holds the statement's start is followed to where it is used as the left
/// operand), up to its <c>WHERE</c> in any case, with <c>--</c> and <c>/* */</c>
/// comments removed. Every assignment of one of the three columns in its SET list
/// (bare, bracketed or table-qualified; a column that opens a SET item, not one
/// compared inside a CASE) is attributed to the member that holds the statement, and
/// the set of members is pinned.
/// </para>
/// <para>
/// Not read: an aliased <c>UPDATE u SET … FROM Users u</c> or a MERGE that writes
/// through its alias (none exists), an interpolated string, SQL built at run time,
/// and the stored procedures under <c>Auth_DB</c> (the two dead ones that write the
/// lock columns leave with the second part of X12).
/// </para>
/// <para>
/// Deliberate breaks: put <c>[Status] = @Status</c> back into UpdateAsync; revert the
/// table pattern to the bracketed forms (the bare <c>UPDATE Users</c> row turns red).
/// </para>
/// </remarks>
public class UserRepositoryLockColumnsSourceGuardTests
{
    private static readonly HashSet<string> AllowedWriters =
    [
        "RecordSuccessfulPasswordLoginSql",
        "ClearAutomaticLockSql",
        "RecordFailedLoginSql",
        "RepairLapsedLockSql",
        "SetAccountStatusSql",
    ];

    private static readonly Regex UsersWrite = new(
        @"(?:UPDATE|MERGE(?:\s+INTO)?)\s+(?:(?:\[dbo\]|dbo)\s*\.\s*)?(?:\[Users\]|Users)(?![\w\]])",
        RegexOptions.IgnoreCase);

    private static readonly Regex LockColumn = new(
        @"(?:(?:(?:\[dbo\]|dbo)\s*\.\s*)?(?:\[Users\]|Users)\s*\.\s*)?\[?\b(Status|FailedLoginAttempts|LockoutEndUtc)\b\]?\s*=(?!=)",
        RegexOptions.IgnoreCase);

    private static readonly Regex Where = new(@"\bWHERE\b", RegexOptions.IgnoreCase);

    private static readonly Regex SetKeyword = new(@"\bSET\b", RegexOptions.IgnoreCase);

    private static readonly Regex SqlComment = new(@"--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline);

    private static readonly Regex ConstantDeclaration = new(@"\bconst\s+string\s+(?<name>\w+)\s*=\s*");

    private static readonly Regex Member = new(
        @"\b(?:const\s+string|Task(?:<[^\r\n]*?>)?)\s+(\w+)\s*(?:=|\()");

    /// <summary>Every assignment of a lock column inside an UPDATE or MERGE of Users in <paramref name="source"/>.</summary>
    internal static List<(string Member, string Column)> Assignments(string source)
    {
        var found = new List<(string, string)>();
        foreach (var (at, sql) in Statements(source))
        {
            var owner = Member.Matches(source[..at]).LastOrDefault()?.Groups[1].Value ?? "?";
            found.AddRange(AssignedLockColumns(sql).Select(column => (owner, column)));
        }

        return found;
    }

    /// <summary>
    /// The lock columns one UPDATE or MERGE of Users assigns, read from its start up to
    /// its <c>WHERE</c>, with SQL comments removed.
    /// </summary>
    private static IEnumerable<string> AssignedLockColumns(string sql)
    {
        var where = Where.Match(sql);
        var statement = SqlComment.Replace(where.Success ? sql[..where.Index] : sql, " ");
        foreach (Match column in LockColumn.Matches(statement))
        {
            // An assignment opens a SET item: nothing but layout between it and
            // the SET keyword or the previous comma. A comparison inside a CASE
            // has WHEN, AND, OR or a parenthesis before it.
            var before = statement[..column.Index];
            var set = SetKeyword.Matches(before).LastOrDefault();
            var boundary = Math.Max(before.LastIndexOf(','), set is null ? -1 : set.Index + set.Length - 1);
            if (before[(boundary + 1)..].Trim().Length == 0)
            {
                yield return column.Groups[1].Value;
            }
        }
    }

    /// <summary>
    /// X12 PR B: every assignment of a lock column inside an UPDATE or MERGE of Users in
    /// a SQL script (a procedure, a seed, an upgrade): the statement from its start to
    /// its own <c>WHERE</c>, or to the end of its batch when it has none.
    /// </summary>
    internal static List<string> SqlAssignments(string script)
    {
        var found = new List<string>();
        script = SqlComment.Replace(script, " ");
        foreach (Match write in UsersWrite.Matches(script))
        {
            var rest = script[write.Index..];
            var batchEnd = Regex.Match(rest, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            found.AddRange(AssignedLockColumns(batchEnd.Success ? rest[..batchEnd.Index] : rest));
        }

        return found;
    }

    /// <summary>
    /// Where each UPDATE or MERGE of Users starts in <paramref name="source"/>, and the
    /// SQL its C# expression builds from there; and, for a constant that holds such a
    /// start, each place it is the left operand of a concatenation.
    /// </summary>
    private static IEnumerable<(int At, string Sql)> Statements(string source)
    {
        foreach (Match write in UsersWrite.Matches(source))
        {
            yield return (write.Index, ReadExpression(source, write.Index, inLiteral: true, depth: 0));
        }

        foreach (Match constant in ConstantDeclaration.Matches(source))
        {
            var name = constant.Groups["name"].Value;
            var value = ReadExpression(source, constant.Index + constant.Length, inLiteral: false, depth: 0);
            if (!UsersWrite.IsMatch(value))
            {
                continue;
            }

            foreach (Match use in Regex.Matches(source, $@"\b{name}\s*\+"))
            {
                yield return (use.Index, value + ReadExpression(source, use.Index + name.Length, inLiteral: false, depth: 0));
            }
        }
    }

    /// <summary>
    /// The text a C# string expression builds from <paramref name="index"/> on: string
    /// literals (verbatim or not) joined by <c>+</c>, each <c>const string</c> operand
    /// replaced by its value, up to the first token that ends the expression.
    /// </summary>
    internal static string ReadExpression(string source, int index, bool inLiteral, int depth)
    {
        var sql = new StringBuilder();
        var i = index;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (inLiteral)
            {
                if (c == '\\' && next is 'n' or 'r' or 't')
                {
                    sql.Append(' ');
                    i += 2;
                }
                else if (c == '"' && next == '"')
                {
                    sql.Append('"');
                    i += 2;
                }
                else if (c == '"')
                {
                    inLiteral = false;
                    i++;
                }
                else
                {
                    sql.Append(c);
                    i++;
                }

                continue;
            }

            if (char.IsWhiteSpace(c) || c is '+' or '@')
            {
                i++;
            }
            else if (c == '"')
            {
                inLiteral = true;
                i++;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var name = Regex.Match(source[i..], @"^\w+").Value;
                i += name.Length;
                var declaration = Regex.Match(source, $@"\bconst\s+string\s+{name}\s*=\s*");
                if (declaration.Success && depth < 5)
                {
                    sql.Append(' ').Append(ReadExpression(source, declaration.Index + declaration.Length, false, depth + 1)).Append(' ');
                }
            }
            else
            {
                break;
            }
        }

        return sql.ToString();
    }

    private static List<(string File, string Member, string Column)> Writers() =>
        ApiSourceScan.ProductionSources()
            .SelectMany(file => Assignments(file.Source).Select(w => (Path.GetFileName(file.File), w.Member, w.Column)))
            .ToList();

    [Fact]
    public void OnlyTheLockStatementsWriteTheLockColumns()
    {
        var writers = Writers();

        writers.Should().NotBeEmpty("the scan must find the lock statements themselves, or it guards nothing");
        writers.Select(w => w.File).Distinct().Should().Equal(["UserRepository.cs"],
            "no other file may write the lock columns of Users");
        writers.Select(w => w.Member).Distinct().Should().BeEquivalentTo(AllowedWriters,
            "a new writer of Status, the count or the expiry must be one of the lock statements");
    }

    [Fact]
    public void UpdateAsync_WritesNoLockColumn()
    {
        Writers().Should().NotContain(w => w.Member == "UpdateAsync");
    }

    [Theory]
    [InlineData("const string A = \"UPDATE [dbo].[Users] SET [Status] = 1 WHERE [Id] = @Id\";", "Status")]
    [InlineData("const string A = @\"UPDATE dbo.Users SET Email = @E, LockoutEndUtc = NULL WHERE Id = @Id\";", "LockoutEndUtc")]
    [InlineData("const string A = \"UPDATE [dbo].[Users]\\n\" +\n    \"SET [MustChangePassword] = 0,\\n\" +\n    \"    [FailedLoginAttempts] = 0\\n\" +\n    \"WHERE [Id] = @Id\";", "FailedLoginAttempts")]
    // Review 64, F6: the shapes the first version of this guard could not read.
    [InlineData("const string A = \"UPDATE Users SET Status = 1 WHERE Id = @Id\";", "Status")]
    [InlineData("const string A = \"UPDATE [dbo].Users SET [FailedLoginAttempts] = 0 WHERE [Id] = @Id\";", "FailedLoginAttempts")]
    [InlineData("const string A = \"UPDATE dbo.[Users] SET [LockoutEndUtc] = NULL WHERE [Id] = @Id\";", "LockoutEndUtc")]
    [InlineData("const string A = \"update users set status = 1 where id = @Id\";", "status")]
    [InlineData("const string A = @\"UPDATE [dbo].[Users] SET -- reset the lock\n    [Status] = 1 WHERE [Id] = @Id\";", "Status")]
    [InlineData("const string A = \"UPDATE [dbo].[Users] SET [Email] = @E, /* and the lock */ [Status] = 1 WHERE [Id] = @Id\";", "Status")]
    [InlineData("const string A = \"UPDATE [dbo].[Users] SET [Users].[Status] = 1 WHERE [Id] = @Id\";", "Status")]
    [InlineData("const string A = \"UPDATE dbo.Users SET dbo.Users.LockoutEndUtc = NULL WHERE Id = @Id\";", "LockoutEndUtc")]
    [InlineData("const string A = \"MERGE [dbo].[Users] AS t USING (SELECT @Id AS Id) AS s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET Status = 1;\";", "Status")]
    [InlineData("const string Head = \"UPDATE Users SET \";\nconst string A = Head + \"[Status] = 1 WHERE Id = @Id\";", "Status")]
    [InlineData("const string Clause = \"[Status] = 1\";\nconst string A = \"UPDATE Users SET [Email] = @E, \" + Clause;", "Status")]
    public void Scanner_FindsTheOtherShapesAWriteCouldTake(string source, string column)
    {
        Assignments(source).Should().ContainSingle(w => w.Member == "A" && w.Column == column);
    }

    [Fact]
    public void Scanner_IgnoresAComparisonInsideACase()
    {
        const string source = "const string A = @\"UPDATE [dbo].[Users] SET [ModifiedAt] = CASE WHEN [Status] = 3 THEN 1 ELSE 2 END WHERE [Id] = @Id\";";

        Assignments(source).Should().BeEmpty();
    }

    [Fact]
    public void Scanner_StopsAtALowercaseWhere()
    {
        // What follows the Users statement's where belongs to it no more.
        const string source = "const string A = \"UPDATE Users SET Email = @E where Id = @Id; UPDATE Logs SET Status = 1\";";

        Assignments(source).Should().BeEmpty();
    }

    [Fact]
    public void Scanner_LeavesOtherTables()
    {
        const string source = "const string A = \"UPDATE [dbo].[UserSessions] SET [Status] = 1 WHERE [Id] = @Id\";";

        Assignments(source).Should().BeEmpty();
    }

    // ── X12 PR B: the database project's own scripts ─────────────────────────
    //
    // The two dead procedures that wrote the lock columns (sp_CheckAccountLockout,
    // which zeroed the count when a lock lapsed, and sp_RecordLoginAttempt) left
    // the project, so the scan now covers every .sql under Auth_DB too, with no
    // allow-list: nothing there writes the lock columns. Deliberate break: restore
    // sp_CheckAccountLockout.sql.

    private static readonly string[] DeadProcedures = ["sp_CheckAccountLockout", "sp_RecordLoginAttempt", "sp_ValidateCredentials"];

    private static IEnumerable<string> DatabaseScripts() =>
        Directory.EnumerateFiles(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB"), "*.sql", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    [Fact]
    public void NoDatabaseScript_WritesTheLockColumns()
    {
        var scripts = DatabaseScripts().ToList();
        scripts.Should().HaveCountGreaterThan(30, "the scan must read the project's tables, procedures and seeds, or it guards nothing");

        var writers = scripts
            .SelectMany(path => SqlAssignments(File.ReadAllText(path)).Select(column => $"{Path.GetFileName(path)}: {column}"))
            .ToList();

        writers.Should().BeEmpty("Status, the count and the expiry are written by the API's lock statements alone");
    }

    [Theory]
    [InlineData("CREATE PROCEDURE [dbo].[p] AS\nBEGIN\n    UPDATE [dbo].[Users]\n    SET [FailedLoginAttempts] = 0,\n        [LockoutEndUtc] = NULL\n    WHERE [Id] = @UserId;\nEND\nGO", 2)]
    [InlineData("UPDATE dbo.Users SET Status = 1 -- unlock\nGO\nUPDATE dbo.Roles SET Status = 1", 1)]
    [InlineData("update users set lockoutendutc = null where id = @id", 1)]
    [InlineData("-- UPDATE [dbo].[Users] SET [Status] = 1\nSELECT 1", 0)]
    [InlineData("UPDATE [dbo].[Users] SET [ModifiedAt] = CASE WHEN [Status] = 3 THEN 1 END WHERE [Id] = @Id", 0)]
    public void SqlScanner_ReadsTheShapesAScriptWriteTakes(string script, int expected)
    {
        SqlAssignments(script).Should().HaveCount(expected);
    }

    [Fact]
    public void TheDeadProcedures_AreDefinedNowhere_AndDroppedFromExistingDatabases()
    {
        var create = new Regex(
            $@"\bCREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+(?:\[?dbo\]?\s*\.\s*)?\[?(?:{string.Join("|", DeadProcedures)})\b",
            RegexOptions.IgnoreCase);
        DatabaseScripts().Where(path => create.IsMatch(File.ReadAllText(path))).Select(Path.GetFileName)
            .Should().BeEmpty("a fresh database must never get them back");

        var root = ApiSourceScan.SolutionDirectory();
        var project = File.ReadAllText(Path.Combine(root, "Auth_DB", "Auth_DB.sqlproj"));
        project.Should().NotContainAny(DeadProcedures);

        // DropObjectsNotInSource is False, so only an explicit drop reaches an
        // existing database; the post-deployment script runs it on every publish.
        const string upgrade = "2026-10-12_DropDeadLockProcedures.sql";
        var script = File.ReadAllText(Path.Combine(root, "Auth_DB", "dbo", "Scripts", "Upgrades", upgrade));
        foreach (var procedure in DeadProcedures)
        {
            script.Should().Contain($"DROP PROCEDURE IF EXISTS [dbo].[{procedure}];", "idempotent: a no-op where it is gone");
        }

        script.Should().MatchRegex(@"\A[\x00-\x7F]*\z", "an :r-included script is pure ASCII");
        File.ReadAllBytes(Path.Combine(root, "Auth_DB", "dbo", "Scripts", "Upgrades", upgrade)).Take(3)
            .Should().NotEqual([0xEF, 0xBB, 0xBF], "and has no BOM");
        project.Should().Contain($@"<None Include=""dbo\Scripts\Upgrades\{upgrade}"" />");
        File.ReadAllText(Path.Combine(root, "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql"))
            .Should().Contain($@":r ..\Scripts\Upgrades\{upgrade}");
    }
}
