using System.Text.RegularExpressions;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-155: the two RefreshTokens statements single logout adds, pinned as text. Each condition is
/// a scope: dropping one ends, or moves, another person's or another browser's sign-ins.
/// </summary>
public class RefreshTokenSingleLogoutSqlTests
{
    private static string Sql(RecordingDbConnectionFactory db) =>
        Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");

    [Fact]
    public async Task GetLiveByIdpSessionAsync_ReadsOnlyThatUsersUnrevokedTokensOfThatIdpSession()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var userId = Guid.NewGuid();
        var idpSessionId = Guid.NewGuid();

        var live = await new RefreshTokenRepository(db).GetLiveByIdpSessionAsync(userId, idpSessionId, CancellationToken.None);

        live.Should().BeEmpty();
        var sql = Sql(db);
        sql.Should().Contain("FROM [dbo].[RefreshTokens]");
        sql.Should().Contain("WHERE [UserId] = @UserId", "IX_RefreshTokens_UserId, and only the IdP session's own person");
        sql.Should().Contain("AND [IdpSessionId] = @IdpSessionId", "another browser's sign-ins stay up");
        sql.Should().Contain("AND [RevokedAt] IS NULL");
        sql.Should().NotContain("ExpiresAt", "an expired token's session may still have an access token out");
        db.LastCommand!.Parameters["UserId"].Should().Be(userId);
        db.LastCommand.Parameters["IdpSessionId"].Should().Be(idpSessionId);
    }

    [Fact]
    public async Task RelinkApplicationChainsAsync_MovesOnlyThatUsersLiveApplicationTokens()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 2);
        var userId = Guid.NewGuid();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var moved = await new RefreshTokenRepository(db).RelinkApplicationChainsAsync(userId, from, to, CancellationToken.None);

        moved.Should().Be(2);
        var sql = Sql(db);
        sql.Should().Contain("UPDATE [dbo].[RefreshTokens] SET [IdpSessionId] = @ToIdpSessionId");
        sql.Should().Contain("WHERE [IdpSessionId] = @FromIdpSessionId");
        sql.Should().Contain("AND [UserId] = @UserId");
        sql.Should().Contain("AND [ApplicationId] IS NOT NULL", "a platform app's own chain never moves from a session that is over");
        sql.Should().Contain("AND [RevokedAt] IS NULL");
        db.LastCommand!.Parameters["UserId"].Should().Be(userId);
        db.LastCommand.Parameters["FromIdpSessionId"].Should().Be(from);
        db.LastCommand.Parameters["ToIdpSessionId"].Should().Be(to);
    }
}
