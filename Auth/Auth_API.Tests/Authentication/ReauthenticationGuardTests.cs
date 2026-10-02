using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for ReauthenticationGuard: a change to the second factor needs a
/// session that is the user's own, still active, and signed in no longer ago than
/// TwoFactor:ReauthenticationMaxAgeMinutes. The session row's creation time is the
/// sign-in time — a refreshed token keeps its session — so a bearer token kept
/// alive by refreshing is still an old sign-in.
/// </summary>
public class ReauthenticationGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly TwoFactorSettings _settings = new();
    private readonly ReauthenticationGuard _guard;

    public ReauthenticationGuardTests()
    {
        _guard = new ReauthenticationGuard(
            _sessionRepositoryMock.Object,
            TestHelpers.CreateOptions(_settings),
            new FixedTimeProvider(Now),
            Mock.Of<ILogger<ReauthenticationGuard>>());
    }

    private static UserSession Session(
        Guid sessionId,
        Guid userId,
        TimeSpan age,
        bool isActive = true,
        string? deviceName = "Chrome on Windows") =>
        new(
            sessionId,
            userId,
            applicationId: null,
            refreshTokenId: null,
            sessionTokenHash: "hash",
            ipAddress: "203.0.113.7",
            userAgent: "agent",
            DeviceType.Desktop,
            deviceId: null,
            deviceName,
            deviceHash: null,
            location: null,
            createdAt: Now.UtcDateTime - age,
            expiresAt: Now.UtcDateTime.AddDays(7),
            lastActivityAt: Now.UtcDateTime,
            isActive,
            terminatedAt: isActive ? null : Now.UtcDateTime,
            terminationReason: isActive ? null : "Logout");

    private void GivenSession(UserSession session) =>
        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    private static void ShouldRequireReauthentication(ErrorOr<RecentSession> result)
    {
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task EnsureRecentSignIn_RecentSessionOfTheUser_ReturnsItsDevice()
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(14));
        GivenSession(session);

        var result = await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.SessionId.Should().Be(session.Id);
        result.Value.DeviceName.Should().Be("Chrome on Windows");
    }

    [Fact]
    public async Task EnsureRecentSignIn_ExactlyAtTheLimit_IsStillRecent()
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(15));
        GivenSession(session);

        var result = await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureRecentSignIn_OlderThanTheLimit_RequiresReauthentication()
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        GivenSession(session);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignIn_NoSessionId_RequiresReauthentication_WithoutAQuery()
    {
        // A token with no session id cannot show when its sign-in happened.
        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(Guid.NewGuid(), null, CancellationToken.None));
        _sessionRepositoryMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureRecentSignIn_NoSessionRow_RequiresReauthentication()
    {
        // A legacy token's jti names no session row: fail closed.
        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignIn_AnotherUsersSession_RequiresReauthentication()
    {
        var session = Session(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(1));
        GivenSession(session);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(Guid.NewGuid(), session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignIn_EndedSession_RequiresReauthentication()
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(1), isActive: false);
        GivenSession(session);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignIn_ReadsTheLimitPerCall()
    {
        // Hot: an operator who shortens the window during an incident does not
        // have to restart the API.
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(10));
        GivenSession(session);

        (await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();

        _settings.ReauthenticationMaxAgeMinutes = 5;

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 6, true)]       // below the range: read as 5 minutes, so 6 is too old
    [InlineData(-30, 6, true)]
    [InlineData(1000, 61, true)]   // above the range: read as 60 minutes, so 61 is too old
    [InlineData(int.MaxValue, 61, true)]
    [InlineData(1000, 59, false)]
    public async Task EnsureRecentSignIn_NoValueTurnsTheCheckOff(int configured, int ageMinutes, bool refused)
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(ageMinutes));
        GivenSession(session);
        _settings.ReauthenticationMaxAgeMinutes = configured;

        var result = await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None);

        result.IsError.Should().Be(refused);
    }
}
