using System.Text.RegularExpressions;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the refresh's one write to its session row, <c>TouchOnRefreshAsync</c>
/// (OI-97, OI-103).
/// <para>
/// Three facts about the SQL hold the fix, and losing any one silently reopens a
/// hole: without <c>[EndedAt] IS NULL</c> a refresh racing a sign-out revives the
/// session; without <c>[UserId] = @UserId</c> a token could touch a row of another
/// user; and without the CASE a refresh racing another, or one with rotation off,
/// moves the expiry back toward the sweep. The test project has no database, so
/// the recorded command is the unit under test.
/// </para>
/// </summary>
public class UserSessionRepositorySqlGuardTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = Now.AddDays(7);

    private static async Task<RecordedCommand> TouchAsync()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new UserSessionRepository(db).TouchOnRefreshAsync(
            SessionId, UserId, Now, ExpiresAt, CancellationToken.None);

        db.Commands.Should().ContainSingle("the touch is one statement");
        return db.Commands[0];
    }

    private static string SetClause(string sql)
    {
        var set = Regex.Match(sql, @"\bSET\b([\s\S]*?)\bWHERE\b");
        set.Success.Should().BeTrue("the touch is an UPDATE … SET … WHERE");
        return set.Groups[1].Value;
    }

    [Fact]
    public async Task TheTouch_UpdatesOnlyALiveRowOfTheTokensOwner()
    {
        var command = await TouchAsync();

        command.CommandText.Should().MatchRegex(
            @"UPDATE\s+\[dbo\]\.\[UserSessions\]\s+SET[\s\S]*?WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[UserId\]\s*=\s*@UserId\s+AND\s+\[EndedAt\]\s+IS\s+NULL\s*$",
            "an ended row stays ended, and a row of another user is never touched");
    }

    [Fact]
    public async Task TheTouch_NeverWritesTheRowsEnd()
    {
        var set = SetClause((await TouchAsync()).CommandText);

        set.Should().NotContain("[EndedAt]");
        set.Should().NotContain("[EndReason]");
    }

    [Fact]
    public async Task TheExpiry_OnlyMovesForward()
    {
        var set = SetClause((await TouchAsync()).CommandText);

        set.Should().MatchRegex(
            @"\[ExpiresAt\]\s*=\s*CASE\s+WHEN\s+@ExpiresAt\s*>\s*\[ExpiresAt\]\s+THEN\s+@ExpiresAt\s+ELSE\s+\[ExpiresAt\]\s+END",
            "a racing refresh, or one with rotation off, must not shorten the row");
        Regex.Matches(set, @"\[ExpiresAt\]\s*=").Count.Should().Be(1, "no second assignment may override the CASE");
    }

    [Fact]
    public async Task TheTouch_RecordsTheActivity_AndBindsEveryValue()
    {
        var command = await TouchAsync();

        SetClause(command.CommandText).Should().MatchRegex(@"\[LastActivityAt\]\s*=\s*@Now");
        command.Parameters["Id"].Should().Be(SessionId);
        command.Parameters["UserId"].Should().Be(UserId);
        command.Parameters["Now"].Should().Be(Now);
        command.Parameters["ExpiresAt"].Should().Be(ExpiresAt);
    }
}
