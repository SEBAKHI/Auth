using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="CredentialRevocationService"/> — the shared
/// credential-kill primitive: session termination + per-session refresh-token
/// revocation + session-id blacklisting, and the full wipe used by deletion.
/// </summary>
public class CredentialRevocationServiceTests
{
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<IIdpSessionRepository> _idpSessionRepositoryMock = new();
    private readonly Mock<ITokenBlacklistService> _blacklistServiceMock = new();
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock = new();

    // Not the defaults, so a horizon computed from anything else shows.
    private readonly Auth.Application.Configuration.JwtSettings _jwtSettings = new()
    {
        AccessTokenLifetimeMinutes = 20,
        ClockSkewSeconds = 90
    };

    private readonly CredentialRevocationService _service;

    public CredentialRevocationServiceTests()
    {
        // The hash is what storage is keyed by, so the spare-this-one tests need a
        // deterministic stand-in rather than a real HMAC.
        _tokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string token) => $"hash:{token}");

        _service = new CredentialRevocationService(
            _sessionRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _idpSessionRepositoryMock.Object,
            _blacklistServiceMock.Object,
            _tokenKeyServiceMock.Object,
            TestHelpers.CreateOptions(_jwtSettings),
            new Mock<ILogger<CredentialRevocationService>>().Object);
    }

    private void SetupActiveSessions(Guid userId, params Auth.Domain.Entities.UserSession[] sessions)
    {
        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions.ToList());
    }

    [Fact]
    public async Task EnforceConcurrentSessionLimitAsync_KillsEachEvictedSessionForReal()
    {
        // Ending the row is bookkeeping. Until the refresh token is revoked the
        // evicted device simply refreshes and comes back, and until the session
        // id is blacklisted its existing access token keeps working for the rest
        // of its lifetime — so the "limit" would let a user hold any number of
        // usable credentials.
        var userId = Guid.NewGuid();
        var evicted = new[]
        {
            TestHelpers.CreateUserSession(userId: userId),
            TestHelpers.CreateUserSession(userId: userId)
        };
        _sessionRepositoryMock
            .Setup(r => r.TerminateBeyondLimitAsync(userId, 3, "session_limit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(evicted);

        var result = await _service.EnforceConcurrentSessionLimitAsync(
            userId, 3, "session_limit", CancellationToken.None);

        result.Should().HaveCount(2);
        foreach (var session in evicted)
        {
            _refreshTokenRepositoryMock.Verify(
                r => r.RevokeBySessionIdAsync(session.Id, userId, "session_limit", It.IsAny<CancellationToken>()),
                Times.Once);
            _blacklistServiceMock.Verify(
                b => b.BlacklistSession(session.Id.ToString(), session.ExpiresAt), Times.Once);
        }
    }

    [Fact]
    public async Task EnforceConcurrentSessionLimitAsync_WithinTheLimit_TouchesNothing()
    {
        var userId = Guid.NewGuid();
        _sessionRepositoryMock
            .Setup(r => r.TerminateBeyondLimitAsync(userId, 5, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.EnforceConcurrentSessionLimitAsync(
            userId, 5, "session_limit", CancellationToken.None);

        result.Should().BeEmpty();
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blacklistServiceMock.Verify(
            b => b.BlacklistSession(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EnforceConcurrentSessionLimitAsync_NonPositiveLimit_NeverReachesTheDatabase(int maxSessions)
    {
        // 0 means unlimited. Reaching the repository at all here would put a
        // ranking query on every single sign-in for the default configuration,
        // and a negative value would end every session the user has.
        var result = await _service.EnforceConcurrentSessionLimitAsync(
            Guid.NewGuid(), maxSessions, "session_limit", CancellationToken.None);

        result.Should().BeEmpty();
        _sessionRepositoryMock.Verify(
            r => r.TerminateBeyondLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TerminateSessionsAsync_NoExcept_TerminatesAllRevokesAndBlacklistsEach()
    {
        var userId = Guid.NewGuid();
        var sessions = new[]
        {
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true)
        };
        SetupActiveSessions(userId, sessions);

        var count = await _service.TerminateSessionsAsync(userId, null, userId, "reason", CancellationToken.None);

        count.Should().Be(3);
        _sessionRepositoryMock.Verify(
            r => r.TerminateAllForUserAsync(userId, "reason", It.IsAny<CancellationToken>()), Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateOtherSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        foreach (var session in sessions)
        {
            _refreshTokenRepositoryMock.Verify(
                r => r.RevokeBySessionIdAsync(session.Id, userId, "reason", It.IsAny<CancellationToken>()), Times.Once);
            _blacklistServiceMock.Verify(
                b => b.BlacklistSession(session.Id.ToString(), session.ExpiresAt), Times.Once);
        }
    }

    [Fact]
    public async Task TerminateSessionsAsync_WithExcept_SparesTheCurrentSession()
    {
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var current = TestHelpers.CreateUserSession(id: currentSessionId, userId: userId, isActive: true);
        var other = TestHelpers.CreateUserSession(userId: userId, isActive: true);
        SetupActiveSessions(userId, current, other);

        var count = await _service.TerminateSessionsAsync(userId, currentSessionId, userId, "reason", CancellationToken.None);

        count.Should().Be(1);
        _sessionRepositoryMock.Verify(
            r => r.TerminateOtherSessionsAsync(userId, currentSessionId, "reason", It.IsAny<CancellationToken>()), Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateAllForUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeBySessionIdAsync(currentSessionId, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _blacklistServiceMock.Verify(
            b => b.BlacklistSession(currentSessionId.ToString(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task TerminateSessionsAsync_NoActiveSessions_ReturnsZero()
    {
        var userId = Guid.NewGuid();
        SetupActiveSessions(userId);

        var count = await _service.TerminateSessionsAsync(userId, null, userId, "reason", CancellationToken.None);

        count.Should().Be(0);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeAllCredentialsAsync_AlsoWipesSessionlessRefreshTokensAndIdpSessions()
    {
        var userId = Guid.NewGuid();
        var revokedBy = Guid.NewGuid();
        var sessions = new[]
        {
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true)
        };
        SetupActiveSessions(userId, sessions);

        var count = await _service.RevokeAllCredentialsAsync(userId, revokedBy, "Account deleted", CancellationToken.None);

        count.Should().Be(2);
        _sessionRepositoryMock.Verify(
            r => r.TerminateAllForUserAsync(userId, "Account deleted", It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, revokedBy, "Account deleted", It.IsAny<CancellationToken>()), Times.Once);
        _idpSessionRepositoryMock.Verify(
            r => r.RevokeAllForUserExceptAsync(userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeCredentialsAsync_RevokesTheSsoSessionsToo()
    {
        // The gap this whole change exists to close: ending UserSessions rows
        // evicts nobody, because nothing on the refresh path consults them and the
        // SSO cookie lives in a table of its own.
        var userId = Guid.NewGuid();
        SetupActiveSessions(userId, TestHelpers.CreateUserSession(userId: userId, isActive: true));

        await _service.RevokeCredentialsAsync(
            userId, exceptSessionId: null, exceptIdpSessionToken: null,
            revokedBy: userId, "Password reset", CancellationToken.None);

        _idpSessionRepositoryMock.Verify(
            r => r.RevokeAllForUserExceptAsync(userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeCredentialsAsync_SparesTheCallersOwnBrowser()
    {
        var userId = Guid.NewGuid();
        var currentSession = TestHelpers.CreateUserSession(userId: userId, isActive: true);
        SetupActiveSessions(userId, currentSession);

        await _service.RevokeCredentialsAsync(
            userId, currentSession.Id, exceptIdpSessionToken: "cookie-token",
            revokedBy: userId, "Password changed", CancellationToken.None);

        // Spared by HASH, never by the plain value — that is what storage holds.
        _idpSessionRepositoryMock.Verify(
            r => r.RevokeAllForUserExceptAsync(userId, "hash:cookie-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeCredentialsAsync_SparingASession_LeavesSessionlessTokensAlone()
    {
        // The blanket sweep would take the spared session's own refresh token with
        // it, so it is deliberately skipped whenever something is being kept.
        var userId = Guid.NewGuid();
        var currentSession = TestHelpers.CreateUserSession(userId: userId, isActive: true);
        SetupActiveSessions(userId, currentSession);

        await _service.RevokeCredentialsAsync(
            userId, currentSession.Id, exceptIdpSessionToken: null,
            revokedBy: userId, "Password changed", CancellationToken.None);

        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- One session, and one application's sessions: the blacklist horizon ---

    private (DateTime From, DateTime To) HorizonAround(DateTime before, DateTime after)
    {
        var span = _jwtSettings.AccessTokenLifetime + _jwtSettings.ClockSkew;
        return (before + span, after + span);
    }

    [Fact]
    public async Task TerminateSessionAsync_BlacklistsTheSessionUntilLifetimePlusSkew()
    {
        var sessionId = Guid.NewGuid();
        DateTime? until = null;
        _blacklistServiceMock
            .Setup(b => b.BlacklistSession(sessionId.ToString(), It.IsAny<DateTime>()))
            .Callback((string _, DateTime expiresAt) => until = expiresAt);

        var before = DateTime.UtcNow;
        await _service.TerminateSessionAsync(sessionId, null, "logout", CancellationToken.None);
        var (from, to) = HorizonAround(before, DateTime.UtcNow);

        until.Should().NotBeNull().And.BeOnOrAfter(from).And.BeOnOrBefore(to);
    }

    [Fact]
    public async Task TerminateApplicationSessionsAsync_WholeApplication_RevokesRefreshTokensFirst_ThenBlacklistsEveryEndedSession()
    {
        // Switching an application off: ending the rows alone left every access token
        // the application held working until it expired.
        var applicationId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var ended = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var order = new List<string>();
        var horizons = new List<DateTime>();
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForApplicationAsync(applicationId, actor, "Application deactivated", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("refresh tokens"))
            .Returns(Task.CompletedTask);
        _sessionRepositoryMock
            .Setup(r => r.TerminateForApplicationAsync(applicationId, "Application deactivated", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("rows"))
            .ReturnsAsync(ended);
        _blacklistServiceMock
            .Setup(b => b.BlacklistSession(It.IsAny<string>(), It.IsAny<DateTime>()))
            .Callback((string sid, DateTime expiresAt) =>
            {
                order.Add($"blacklist {sid}");
                horizons.Add(expiresAt);
            });

        var before = DateTime.UtcNow;
        var count = await _service.TerminateApplicationSessionsAsync(
            applicationId, userId: null, actor, "Application deactivated", CancellationToken.None);
        var (from, to) = HorizonAround(before, DateTime.UtcNow);

        count.Should().Be(2);
        // Refresh tokens before the rows: nothing can mint a new token for a session
        // whose id is about to be blacklisted.
        order.Should().Equal(
            "refresh tokens", "rows", $"blacklist {ended[0]}", $"blacklist {ended[1]}");
        horizons.Should().AllSatisfy(h => h.Should().BeOnOrAfter(from).And.BeOnOrBefore(to));
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeForUserAndApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TerminateApplicationSessionsAsync_OneUser_TouchesOnlyThatUsersSessionsOfThatApplication()
    {
        var applicationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var ended = Guid.NewGuid();
        _sessionRepositoryMock
            .Setup(r => r.TerminateForUserAndApplicationAsync(userId, applicationId, "Application access revoked", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ended]);

        var count = await _service.TerminateApplicationSessionsAsync(
            applicationId, userId, actor, "Application access revoked", CancellationToken.None);

        count.Should().Be(1);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeForUserAndApplicationAsync(userId, applicationId, actor, "Application access revoked", It.IsAny<CancellationToken>()),
            Times.Once);
        _blacklistServiceMock.Verify(b => b.BlacklistSession(ended.ToString(), It.IsAny<DateTime>()), Times.Once);
        _blacklistServiceMock.VerifyNoOtherCalls();
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForApplicationAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sessionRepositoryMock.Verify(
            r => r.TerminateForApplicationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TerminateApplicationSessionsAsync_NothingLeftOpen_BlacklistsNothing()
    {
        // A repeat (a retried switch-off) finds no open row and adds nothing.
        var applicationId = Guid.NewGuid();
        _sessionRepositoryMock
            .Setup(r => r.TerminateForApplicationAsync(applicationId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var count = await _service.TerminateApplicationSessionsAsync(
            applicationId, userId: null, Guid.NewGuid(), "Application deactivated", CancellationToken.None);

        count.Should().Be(0);
        _blacklistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeIdpSessionAsync_RevokesExactlyTheSsoSessionOfTheGivenCookie()
    {
        var session = Auth.Domain.Entities.IdpSession.Create(Guid.NewGuid(), "hash:sso", TimeSpan.FromDays(7), null, null);
        _idpSessionRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hash:sso", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _service.RevokeIdpSessionAsync("sso", CancellationToken.None);

        _idpSessionRepositoryMock.Verify(
            r => r.UpdateAsync(It.Is<Auth.Domain.Entities.IdpSession>(s => s == session && s.IsRevoked), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RevokeIdpSessionAsync_WithoutACookie_TouchesNothing(string? cookie)
    {
        await _service.RevokeIdpSessionAsync(cookie, CancellationToken.None);

        _idpSessionRepositoryMock.VerifyNoOtherCalls();
    }
}
