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
/// Read from the source, over every production C# file: each <c>UPDATE</c> of the
/// Users table (bracketed or not, on one line or many, in a verbatim string or a
/// concatenation) is followed to its <c>WHERE</c>, any constant it concatenates is
/// expanded, and every assignment of one of the three columns in its SET list — a
/// column that opens a SET item, not one compared inside a CASE — is attributed to
/// the member that holds the statement. The set of members is pinned. Not covered:
/// an aliased <c>UPDATE u SET … FROM Users u</c> (none exists) and the two dead
/// stored procedures, which leave with the second part of X12.
/// </para>
/// <para>Deliberate break: put <c>[Status] = @Status</c> back into UpdateAsync.</para>
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

    private static readonly Regex UsersUpdate = new(
        @"UPDATE\s+(?:\[dbo\]\.\[Users\]|dbo\.Users|\[Users\])(?![\w\]])", RegexOptions.IgnoreCase);

    private static readonly Regex LockColumn = new(
        @"\[?\b(Status|FailedLoginAttempts|LockoutEndUtc)\b\]?\s*=(?!=)");

    private static readonly Regex Member = new(
        @"\b(?:const\s+string|Task(?:<[^\r\n]*?>)?)\s+(\w+)\s*(?:=|\()");

    /// <summary>Every assignment of a lock column inside an UPDATE of Users in <paramref name="source"/>.</summary>
    internal static List<(string Member, string Column)> Assignments(string source)
    {
        var found = new List<(string, string)>();
        foreach (Match update in UsersUpdate.Matches(source))
        {
            var where = source.IndexOf("WHERE", update.Index, StringComparison.Ordinal);
            var statement = source[update.Index..(where < 0 ? source.Length : where)];

            // Expand constants the statement concatenates (the shared clearing clause).
            foreach (Match part in Regex.Matches(statement, @"\+\s*(\w+)\s*\+"))
            {
                var constant = Regex.Match(
                    source, $@"const\s+string\s+{part.Groups[1].Value}\s*=\s*@""(?<body>[^""]*)""");
                if (constant.Success) statement += ",\n" + constant.Groups["body"].Value;
            }

            var owner = Member.Matches(source[..update.Index]).LastOrDefault()?.Groups[1].Value ?? "?";
            foreach (Match column in LockColumn.Matches(statement))
            {
                // An assignment opens a SET item: nothing but layout between it and
                // the SET keyword or the previous comma. A comparison inside a CASE
                // has WHEN, AND, OR or a parenthesis before it.
                var before = statement[..column.Index];
                var boundary = Math.Max(before.LastIndexOf(','), before.LastIndexOf("SET", StringComparison.OrdinalIgnoreCase) + 2);
                var between = Regex.Replace(before[(boundary + 1)..], @"[\s""+@$]|\\n", string.Empty);
                if (between.Length == 0)
                {
                    found.Add((owner, column.Groups[1].Value));
                }
            }
        }

        return found;
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
}
