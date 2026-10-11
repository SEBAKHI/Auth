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
/// Read from the source, over every production file: each <c>UPDATE [dbo].[Users]</c>
/// is followed to its <c>WHERE</c>, any constant it concatenates is expanded, and
/// every SET line that assigns one of the three columns is attributed to the member
/// that holds the statement. The set of members is pinned. The two dead stored
/// procedures that also write these columns leave with the second part of X12.
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

    private static readonly Regex Assignment = new(
        @"^\s*""?\s*\[(Status|FailedLoginAttempts|LockoutEndUtc)\]\s*=", RegexOptions.Multiline);

    private static readonly Regex Member = new(
        @"\b(?:const\s+string|Task(?:<[^\r\n]*?>)?)\s+(\w+)\s*(?:=|\()");

    private static List<(string File, string Member, string Column)> Writers()
    {
        var writers = new List<(string, string, string)>();
        foreach (var (file, source) in ApiSourceScan.ProductionSources())
        {
            foreach (Match update in Regex.Matches(source, @"UPDATE \[dbo\]\.\[Users\]"))
            {
                var where = source.IndexOf("WHERE", update.Index, StringComparison.Ordinal);
                var statement = source[update.Index..(where < 0 ? source.Length : where)];

                // Expand constants the statement concatenates (the shared clearing clause).
                foreach (Match part in Regex.Matches(statement, @"\+\s*(\w+)\s*\+"))
                {
                    var constant = Regex.Match(
                        source, $@"const\s+string\s+{part.Groups[1].Value}\s*=\s*@""(?<body>[^""]*)""");
                    if (constant.Success) statement += "\n" + constant.Groups["body"].Value;
                }

                var owner = Member.Matches(source[..update.Index]).LastOrDefault()?.Groups[1].Value ?? "?";
                foreach (Match column in Assignment.Matches(statement))
                {
                    writers.Add((Path.GetFileName(file), owner, column.Groups[1].Value));
                }
            }
        }

        return writers;
    }

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
}
