using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-223 T11: the upgrade script that gives assignments of an application-owned role
/// their application's scope. Its behaviour was run on a throwaway LocalDB database
/// (moved, merged with the wider lifetime kept, untouched rows untouched, a second run
/// changing nothing); these pin the shape that behaviour rests on, so an edit that
/// drops the merge (a duplicate-key failure that aborts the whole publish) or loosens
/// the target (rows of global roles, or rows already scoped) goes red here.
/// </summary>
/// <remarks>
/// Deliberate breaks: delete step 2; drop <c>r.[ApplicationId] IS NOT NULL</c> from
/// step 3; run it outside a transaction.
/// </remarks>
public class ScopeApplicationOwnedRoleAssignmentsScriptTests
{
    private const string ScriptName = "2026-10-24_ScopeApplicationOwnedRoleAssignments.sql";

    private static string ScriptPath() => Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "Upgrades", ScriptName);

    /// <summary>The script without comments, on one line.</summary>
    private static string Sql() => Regex.Replace(
        Regex.Replace(File.ReadAllText(ScriptPath()), @"--[^\n]*", " "), @"\s+", " ");

    [Fact]
    public void ThePostDeploymentScript_IncludesIt()
    {
        var postDeployment = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql"));

        postDeployment.Should().Contain($@":r ..\Scripts\Upgrades\{ScriptName}");
    }

    [Fact]
    public void ItWidensThenMergesThenMoves_InOneSerializableTransaction()
    {
        var sql = Sql();

        var begin = sql.IndexOf("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;", StringComparison.Ordinal);
        var widen = sql.IndexOf("UPDATE scoped SET", StringComparison.Ordinal);
        var merge = sql.IndexOf("DELETE unscoped FROM [dbo].[UserRoles] unscoped", StringComparison.Ordinal);
        var move = sql.IndexOf("UPDATE unscoped SET [ApplicationId] = r.[ApplicationId]", StringComparison.Ordinal);
        var commit = sql.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal);

        new[] { begin, widen, merge, move, commit }.Should().OnlyContain(index => index >= 0, "every step is present");
        new[] { begin, widen, merge, move, commit }.Should().BeInAscendingOrder(
            "the scoped row takes the wider lifetime before its unscoped twin goes, and only then do the rest move: moving first would violate UQ_UserRoles");
        sql.Should().Contain("SET XACT_ABORT ON;", "a failure rolls every step back");
    }

    [Fact]
    public void EveryStep_TargetsUnscopedRowsOfApplicationOwnedRolesOnly()
    {
        var sql = Sql();
        var steps = Regex.Matches(sql, @"WHERE unscoped\.\[ApplicationId\] IS NULL AND r\.\[ApplicationId\] IS NOT NULL");

        steps.Should().HaveCount(3, "rows of a global role and rows already scoped are never touched");
        sql.Should().Contain("scoped.[ApplicationId] = r.[ApplicationId]",
            "a duplicate is a row at the role's own application, never one scoped to another");
    }

    [Fact]
    public void TheKeptRow_IsTheWiderLifetime()
    {
        var sql = Sql();

        sql.Should().Contain("scoped.[IsActive] < unscoped.[IsActive]", "an active row wins over an inactive one");
        sql.Should().Contain(
            "unscoped.[IsActive] = scoped.[IsActive] AND scoped.[ExpiresAt] IS NOT NULL AND (unscoped.[ExpiresAt] IS NULL OR unscoped.[ExpiresAt] > scoped.[ExpiresAt])",
            "then no expiry over any date, then the later date");
    }
}
