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

        var session = await new IdpSessionRepository(db).GetByTokenHashAsync("hash", CancellationToken.None);

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

        var code = await new AuthorizationCodeRepository(db).ConsumeByCodeHashAsync("code-hash", CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("INSERTED.[IdpSessionId]");
        code!.IdpSessionId.Should().Be(idpSessionId);
    }

    [Fact]
    public async Task AuthorizationCode_GetByCodeHashAsync_ReadsTheIdpSession()
    {
        var idpSessionId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => CodeRow(idpSessionId));

        var code = await new AuthorizationCodeRepository(db).GetByCodeHashAsync("code-hash", CancellationToken.None);

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

        var token = await new RefreshTokenRepository(db).GetByTokenHashAsync("hash", CancellationToken.None);

        token!.IdpSessionId.Should().Be(idpSessionId);
    }
}
