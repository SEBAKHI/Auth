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
    private readonly CredentialRevocationService _service;

    public CredentialRevocationServiceTests()
    {
        _service = new CredentialRevocationService(
            _sessionRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _idpSessionRepositoryMock.Object,
            _blacklistServiceMock.Object,
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
            r => r.RevokeAllForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
