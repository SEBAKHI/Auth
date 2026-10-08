using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the refresh-token statements OI-65 reads sessions from. A session still in use can have
/// a session row the expiry sweep already ended (its expiry is fixed at sign-in while the refresh
/// chain slides), so it is the refresh tokens, not the row, that say whether a family is alive
/// (the reuse family rule) and which sessions an application's revocation reached (the blacklist).
///
/// The repository is Dapper over raw SQL and this project has no database, so the SQL text is the
/// unit under test.
/// </summary>
public class RefreshTokenSessionSqlTests
{
    public static TheoryData<string> ApplicationRevocations => new()
    {
        "RevokeAllForApplicationAsync",
        "RevokeForUserAndApplicationAsync",
    };

    [Fact]
    public void ApplicationRevocations_IsNotEmpty() =>
        ApplicationRevocations.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(ApplicationRevocations))]
    public void ApplicationRevocation_ReturnsTheSessionOfEveryTokenItRevoked(string method)
    {
        var body = MethodBody($"public async Task<IReadOnlyList<Guid>> {method}(");

        Regex.IsMatch(body, @"UPDATE\s+\[dbo\]\.\[RefreshTokens\]\s+SET").Should().BeTrue();
        body.Should().Contain("OUTPUT inserted.[SessionId]",
            "a live token names a session whose row may already be ended, or never written");
        body.Should().Contain("QueryAsync<Guid?>(", "the OUTPUT rows are read, not discarded");
        body.Should().NotContain("ExecuteAsync(", "an ExecuteAsync would drop the OUTPUT rows");
        body.Should().Contain("[ApplicationId] = @ApplicationId",
            "platform tokens (NULL) and other applications' tokens must survive");
        body.Should().Contain("[RevokedAt] IS NULL", "a token another call revoked is not reported again");
        body.Should().NotContain(" OR ", "an OR would widen the predicate past this application");
    }

    [Fact]
    public void TheOneUserRevocation_IsLimitedToThatUser()
    {
        MethodBody("public async Task<IReadOnlyList<Guid>> RevokeForUserAndApplicationAsync(")
            .Should().Contain("[UserId] = @UserId");
    }

    [Fact]
    public void TheFamilyQuery_AsksForALiveTokenOfThatSession()
    {
        var body = MethodBody("public async Task<bool> HasLiveTokenInSessionAsync(");

        body.Should().Contain("FROM [dbo].[RefreshTokens]", "the session row is no evidence either way");
        body.Should().NotContain("[UserSessions]");
        body.Should().Contain("[SessionId] = @SessionId");
        body.Should().Contain("[RevokedAt] IS NULL");
        body.Should().Contain("[ExpiresAt] > GETUTCDATE()");
        body.Should().NotContain(" OR ");
    }

    /// <summary>The source of the member starting at <paramref name="signature"/>, up to the next member's doc comment.</summary>
    private static string MethodBody(string signature)
    {
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "RefreshTokenRepository.cs")).Replace("\r\n", "\n");

        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "{0} must exist", signature);
        var end = source.IndexOf("\n    /// ", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

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
