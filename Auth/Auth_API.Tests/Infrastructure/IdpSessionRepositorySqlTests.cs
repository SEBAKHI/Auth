using Auth.Domain.Enums;
using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-154 through Dapper: what the repositories send and read back for the four
/// new columns. A recording connection stands in for the database, so Dapper builds
/// the real command and parameters and maps a row shaped like the table's into the
/// real DTO. The activity write and the reads were also run, as written, against a
/// throwaway LocalDB database (see the PR).
/// </summary>
public class IdpSessionRepositorySqlTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    // --- IdpSessions -----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_WritesWhetherTheSessionStaysSignedIn(bool persistent)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var session = IdpSession.Create(
            Guid.NewGuid(), Guid.NewGuid(), "hash", persistent, TimeSpan.FromHours(10), Now, "127.0.0.1", "agent",
            AuthenticationMethods.Password);

        await new IdpSessionRepository(db).CreateAsync(session, CancellationToken.None);

        var command = db.LastCommand!;
        command.CommandText.Should().Contain("[IsPersistent], [LastActivityAt]").And.Contain("@IsPersistent, @LastActivityAt");
        command.Parameters["IsPersistent"].Should().Be(persistent);
        command.Parameters["LastActivityAt"].Should().BeNull();
        command.Parameters["Id"].Should().Be(session.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public async Task GetByTokenHashAsync_ReadsBothColumns_AndNullIsABrowserSession(bool? stored, bool persistent)
    {
        var lastActivity = Now.AddMinutes(-5);
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TokenHash = "hash",
                ExpiresAt = Now.AddHours(10),
                RevokedAt = (DateTime?)null,
                CreatedAt = Now.AddHours(-1),
                IpAddress = (string?)null,
                DeviceInfo = (string?)null,
                IsPersistent = stored,
                LastActivityAt = (DateTime?)lastActivity
            });

        var session = await new IdpSessionRepository(db).GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "hash"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[IsPersistent], [LastActivityAt]");
        session!.IsPersistent.Should().Be(persistent);
        session.LastActivityAt.Should().Be(lastActivity);
    }

    [Fact]
    public async Task RecordActivityAsync_WritesOnlyWhileTheSessionIsAlive_InOneStatement()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var id = Guid.NewGuid();

        var recorded = await new IdpSessionRepository(db).RecordActivityAsync(
            id, Now, TimeSpan.FromMinutes(30), CancellationToken.None);

        recorded.Should().BeTrue();
        db.Commands.Should().ContainSingle("one statement: no read-then-write window");
        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("SET [LastActivityAt] = @Now");
        sql.Should().Contain("WHERE [Id] = @Id");
        sql.Should().Contain("AND [RevokedAt] IS NULL", "a revoked session is never used again");
        sql.Should().Contain("AND [ExpiresAt] > @Now", "nor one past its lifetime");
        sql.Should().Contain(
            "AND ([IsPersistent] = 1 OR @IdleMinutes = 0 OR COALESCE([LastActivityAt], [CreatedAt]) > @IdleCutoff)",
            "nor a browser session that died idle: revoked only lazily, it would otherwise come back");
        db.LastCommand.Parameters["Id"].Should().Be(id);
        db.LastCommand.Parameters["Now"].Should().Be(Now);
        db.LastCommand.Parameters["IdleMinutes"].Should().Be(30);
        db.LastCommand.Parameters["IdleCutoff"].Should().Be(Now.AddMinutes(-30));
    }

    [Fact]
    public async Task RecordActivityAsync_AnswersFalse_WhenNoRowWasAlive()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await new IdpSessionRepository(db).RecordActivityAsync(
            Guid.NewGuid(), Now, TimeSpan.FromMinutes(30), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task RecordActivityAsync_AZeroLimit_IsSentAsZero()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new IdpSessionRepository(db).RecordActivityAsync(Guid.NewGuid(), Now, TimeSpan.Zero, CancellationToken.None);

        db.LastCommand!.Parameters["IdleMinutes"].Should().Be(0);
    }

    [Fact]
    public async Task EndIdleBrowserSessionsAsync_RevokesEveryBrowserSessionIdleSinceTheCutoff_InOneStatement()
    {
        // OI-154 review F1: run before a console change raises the idle limit or
        // switches it off, so a session nobody read since it went idle stays dead.
        var db = new RecordingDbConnectionFactory(affectedRows: 3);
        var cutoff = Now.AddMinutes(-30);

        var ended = await new IdpSessionRepository(db).EndIdleBrowserSessionsAsync(Now, cutoff, CancellationToken.None);

        ended.Should().Be(3);
        db.Commands.Should().ContainSingle("one set-based statement, not a read and a write per session");
        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("UPDATE [dbo].[IdpSessions] SET [RevokedAt] = @Now");
        sql.Should().Contain("WHERE [RevokedAt] IS NULL");
        sql.Should().Contain("AND ISNULL([IsPersistent], 0) = 0", "a «Stay signed in» session has no idle limit; NULL is a browser session");
        sql.Should().Contain("AND [ExpiresAt] > @Now");
        sql.Should().Contain(
            "AND COALESCE([LastActivityAt], [CreatedAt]) <= @IdleCutoff",
            "the complement of the activity write's idle condition");
        db.LastCommand.Parameters["Now"].Should().Be(Now);
        db.LastCommand.Parameters["IdleCutoff"].Should().Be(cutoff);
    }

    [Fact]
    public async Task GetByIdAsync_ReadsTheRow_RevokedOrNot()
    {
        var id = Guid.NewGuid();
        var revokedAt = Now.AddMinutes(-1);
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = id,
                UserId = Guid.NewGuid(),
                TokenHash = "hash",
                ExpiresAt = Now.AddHours(10),
                RevokedAt = (DateTime?)revokedAt,
                CreatedAt = Now.AddHours(-1),
                IpAddress = (string?)null,
                DeviceInfo = (string?)null,
                IsPersistent = (bool?)null,
                LastActivityAt = (DateTime?)null
            });

        var session = await new IdpSessionRepository(db).GetByIdAsync(id, CancellationToken.None);

        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("[IsPersistent], [LastActivityAt]").And.Contain("WHERE [Id] = @Id")
            .And.NotContain("[RevokedAt] IS NULL", "a revoked session must read as revoked, not as missing");
        db.LastCommand.Parameters["Id"].Should().Be(id);
        session!.RevokedAt.Should().Be(revokedAt);
    }

    // --- OI-155 PR B1: the sign-out's lookup reaches a revoked session; the live one does not ---

    [Fact]
    public async Task GetByTokenHashIncludingRevokedAsync_ReadsTheRow_RevokedOrNot()
    {
        var revokedAt = Now.AddMinutes(-1);
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TokenHash = "hash",
                ExpiresAt = Now.AddHours(10),
                RevokedAt = (DateTime?)revokedAt,
                CreatedAt = Now.AddHours(-1),
                IpAddress = (string?)null,
                DeviceInfo = (string?)null,
                IsPersistent = (bool?)false,
                LastActivityAt = (DateTime?)Now.AddMinutes(-40),
                DeviceId = "device-1"
            });

        var session = await new IdpSessionRepository(db).GetByTokenHashIncludingRevokedAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "hash"), CancellationToken.None);

        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ").Trim();
        sql.Should().Contain("[IsPersistent], [LastActivityAt], [DeviceId]")
            .And.EndWith("FROM [dbo].[IdpSessions] WHERE [TokenHash] IN (@Hashes1)",
                "no condition on the row's state: a session a read ended as idle must still be found by a sign-out");
        db.LastCommand.Parameters["Hashes1"].Should().Be("hash");
        session!.RevokedAt.Should().Be(revokedAt);
        session.DeviceId.Should().Be("device-1");
    }

    [Fact]
    public async Task GetByTokenHashAsync_StillReadsOnlyASessionNotRevoked()
    {
        // Every reader of the cookie signs people in through this one: a revoked cookie signs nobody in.
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new IdpSessionRepository(db).GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "hash"), CancellationToken.None);

        Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ")
            .Should().Contain("WHERE [TokenHash] IN (@Hashes1) AND [RevokedAt] IS NULL");
    }

    // --- AuthorizationCodes ------------------------------------------------------

    [Fact]
    public async Task AuthorizationCode_CreateAsync_WritesTheIdpSession()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var idpSessionId = Guid.NewGuid();
        var code = AuthorizationCode.Create(
            Guid.NewGuid(), Guid.NewGuid(), "code-hash", "https://crm.example.com/cb", new string('a', 43),
            TimeSpan.FromSeconds(60), "127.0.0.1", ScopeSet.OpenIdOnly, idpSessionId);

        await new AuthorizationCodeRepository(db).CreateAsync(code, CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[Scope], [IdpSessionId]").And.Contain("@Scope, @IdpSessionId");
        db.LastCommand.Parameters["IdpSessionId"].Should().Be(idpSessionId);
    }

    [Fact]
    public async Task AuthorizationCode_ConsumeByCodeHashAsync_ReturnsTheIdpSession()
    {
        var idpSessionId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => CodeRow(idpSessionId));

        var code = await new AuthorizationCodeRepository(db).ConsumeByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "code-hash"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("INSERTED.[IdpSessionId]");
        code!.IdpSessionId.Should().Be(idpSessionId);
    }

    [Fact]
    public async Task AuthorizationCode_GetByCodeHashAsync_ReadsTheIdpSession()
    {
        var idpSessionId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => CodeRow(idpSessionId));

        var code = await new AuthorizationCodeRepository(db).GetByCodeHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.AuthorizationCode, "code-hash"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[IssuedSessionId], [Scope], [IdpSessionId]");
        code!.IdpSessionId.Should().Be(idpSessionId);
    }

    private static object CodeRow(Guid? idpSessionId) => new
    {
        Id = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        CodeHash = "code-hash",
        RedirectUri = "https://crm.example.com/cb",
        CodeChallenge = new string('a', 43),
        ExpiresAt = Now.AddSeconds(60),
        ConsumedAt = (DateTime?)Now,
        CreatedAt = Now,
        IpAddress = "127.0.0.1",
        IssuedSessionId = (Guid?)null,
        Scope = "openid",
        IdpSessionId = idpSessionId
    };

    // --- RefreshTokens ------------------------------------------------------------

    [Fact]
    public async Task RefreshToken_RelinkIdpSessionAsync_MovesOnlyThatUsersLiveTokens()
    {
        // OI-154 review F2: the same person signing in again in the same browser.
        var db = new RecordingDbConnectionFactory(affectedRows: 2);
        var userId = Guid.NewGuid();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var moved = await new RefreshTokenRepository(db).RelinkIdpSessionAsync(userId, from, to, CancellationToken.None);

        moved.Should().Be(2);
        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("UPDATE [dbo].[RefreshTokens] SET [IdpSessionId] = @ToIdpSessionId");
        sql.Should().Contain("WHERE [IdpSessionId] = @FromIdpSessionId");
        sql.Should().Contain("AND [UserId] = @UserId", "another person's chains end with the session they were born with");
        sql.Should().Contain("AND [RevokedAt] IS NULL");
        db.LastCommand.Parameters["UserId"].Should().Be(userId);
        db.LastCommand.Parameters["FromIdpSessionId"].Should().Be(from);
        db.LastCommand.Parameters["ToIdpSessionId"].Should().Be(to);
    }

    [Fact]
    public async Task RefreshToken_CreateAsync_SendsTheIdpSession()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var idpSessionId = Guid.NewGuid();
        var token = Auth.Domain.Entities.RefreshToken.Create(
            Guid.NewGuid(), "hash", "jti", Guid.NewGuid(), TimeSpan.FromDays(7), "127.0.0.1", null,
            Guid.NewGuid(), "openid", idpSessionId);

        await new RefreshTokenRepository(db).CreateAsync(token, CancellationToken.None);

        db.LastCommand!.Parameters["IdpSessionId"].Should().Be(idpSessionId);
    }

    [Fact]
    public async Task RefreshToken_TryRotateAsync_SendsTheReplacementsIdpSession()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var idpSessionId = Guid.NewGuid();
        var old = TestHelpers.CreateRefreshToken(
            expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid(), idpSessionId: idpSessionId);
        var replacement = old.Rotate("new-hash", "jti", TimeSpan.FromDays(7), "127.0.0.1", old.UserId);

        await new RefreshTokenRepository(db).TryRotateAsync(old, replacement, CancellationToken.None);

        replacement.IdpSessionId.Should().Be(idpSessionId, "the entity's own rotation copies the link");
        db.Commands[1].Parameters["IdpSessionId"].Should().Be(idpSessionId);
    }

    [Fact]
    public async Task RefreshToken_GetByTokenHashAsync_ReadsTheIdpSessionBack()
    {
        var idpSessionId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TokenHash = "hash",
                JwtId = "jti",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IdpSessionId = (Guid?)idpSessionId
            });

        var token = await new RefreshTokenRepository(db).GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.RefreshToken, "hash"), CancellationToken.None);

        token!.IdpSessionId.Should().Be(idpSessionId);
    }
}
