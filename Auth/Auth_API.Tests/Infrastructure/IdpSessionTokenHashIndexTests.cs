using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins <c>IX_IdpSessions_TokenHash</c> without a filter (OI-155). The two explicit ends of a
/// browser's SSO session look it up by its cookie among revoked sessions too, after the live
/// lookup misses. With a <c>WHERE [RevokedAt] IS NULL</c> filter back on the index, every such
/// lookup scans the table, and the end-session confirmation page and the cookie sign-out reach it
/// without a signed-in caller. The file is read as text, because CI does not build the database
/// project.
/// </summary>
public class IdpSessionTokenHashIndexTests
{
    [Fact]
    public void TheTokenHashIndex_CoversEveryRow_RevokedOrNot()
    {
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Authentication", "IdpSessions.sql"));
        var code = string.Join('\n', source.Replace("\r\n", "\n").Split('\n').Select(StripLineComment));

        var statements = Regex.Matches(code, @"CREATE\s+NONCLUSTERED\s+INDEX\s+\[IX_IdpSessions_TokenHash\][^;]*;");

        statements.Should().ContainSingle("one statement declares the index");
        Regex.Replace(statements[0].Value, @"\s+", " ")
            .Should().Be("CREATE NONCLUSTERED INDEX [IX_IdpSessions_TokenHash] ON [dbo].[IdpSessions] ([TokenHash]);",
                "a filter on the index makes the sign-out's lookup of a revoked session scan the table");
    }

    private static string StripLineComment(string line)
    {
        var comment = line.IndexOf("--", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }
}
