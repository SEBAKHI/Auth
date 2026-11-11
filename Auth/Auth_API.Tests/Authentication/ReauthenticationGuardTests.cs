using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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

    // The request's token: issued now unless a test says otherwise (REVIEW-FIXES 152 F23).
    private DateTime? _tokenIssuedAt = Now.UtcDateTime;

    public ReauthenticationGuardTests()
    {
        var accessToken = new Mock<Auth.Application.Interfaces.ICurrentAccessToken>();
        accessToken.SetupGet(token => token.IssuedAtUtc).Returns(() => _tokenIssuedAt);

        _guard = new ReauthenticationGuard(
            _sessionRepositoryMock.Object,
            TestHelpers.CreateOptions(_settings),
            accessToken.Object,
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

    // ── A proof inside the session, with no new sign-in ──

    private void GivenProof(UserSession session, TimeSpan age, AuthenticationMethods methods) =>
        _sessionRepositoryMock
            .Setup(r => r.GetLastProofAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionProof(Now.UtcDateTime - age, methods));

    private UserSession GivenOldSession(AuthenticationMethods methods)
    {
        var userId = Guid.NewGuid();
        var session = TestHelpers.CreateUserSession(
            id: Guid.NewGuid(), userId: userId, createdAt: Now.UtcDateTime.AddHours(-3), methods: methods);
        GivenSession(session);
        return session;
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_AnOldSignIn_WithARecentProof_Passes_WithWhatTheProofProved()
    {
        var session = GivenOldSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        GivenProof(session, TimeSpan.FromMinutes(14), AuthenticationMethods.Password);

        var result = await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Methods.Should().Be(
            AuthenticationMethods.Password, "what that proof proved, never what the session ever proved");
    }

    // ── The time of what the session carries (OI-280 PR B: T-B2, D-B2) ──
    //
    // Deliberate break, verified red: the proof's branch dated at the session's sign-in.

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentSignIn_CarriesTheSignInsTime()
    {
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(3));
        GivenSession(session);

        var result = await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None);

        result.Value.ProvedAtUtc.Should().Be(
            Now.UtcDateTime.AddMinutes(-3), "the sign-in is when what the row first recorded was proved");
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_PastTheSignIn_CarriesTheProofsOwnTime()
    {
        // Row 366: an emailed code given in the window counts at its own time, hours after the sign-in.
        var code = AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false);
        var session = GivenOldSession(code);
        GivenProof(session, TimeSpan.FromMinutes(2), code);

        var result = await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None);

        result.Value.Methods.Should().Be(code);
        result.Value.ProvedAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(-2), "the proof's own time, never the sign-in's");
    }

    // ── A proof counts only for the tokens issued after it (REVIEW-FIXES 152 F23) ──
    //
    // Deliberate break, verified red: the guard's comparison of the token's iat with the proof
    // dropped (a proof then serves every copy of the session's token).

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentProof_RefusesACopyOfTheTokenIssuedBeforeIt()
    {
        // A copy taken from the session before the owner proved themself in their own browser.
        var session = GivenOldSession(AuthenticationMethods.Password);
        GivenProof(session, TimeSpan.FromMinutes(5), AuthenticationMethods.Password);
        _tokenIssuedAt = Now.UtcDateTime.AddMinutes(-6);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None));
        ShouldRequireReauthentication(await _guard.EnsureRecentTwoFactorSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentProof_PassesTheTokenIssuedAfterIt()
    {
        // The browser that proved refreshes its token before the change goes on.
        var session = GivenOldSession(AuthenticationMethods.Password);
        GivenProof(session, TimeSpan.FromMinutes(5), AuthenticationMethods.Password);
        _tokenIssuedAt = Now.UtcDateTime.AddMinutes(-4);

        (await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_ATokenIssuedInTheProofsOwnSecond_Passes()
    {
        // iat holds whole seconds: the refresh right after a proof at 11:55:00.800 is iat 11:55:00.
        var session = GivenOldSession(AuthenticationMethods.Password);
        _sessionRepositoryMock
            .Setup(r => r.GetLastProofAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionProof(Now.UtcDateTime.AddMinutes(-5).AddMilliseconds(800), AuthenticationMethods.Password));
        _tokenIssuedAt = Now.UtcDateTime.AddMinutes(-5);

        (await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();

        _tokenIssuedAt = Now.UtcDateTime.AddMinutes(-5).AddSeconds(-1);
        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentProof_AndATokenThatNamesNoTime_FailsClosed()
    {
        var session = GivenOldSession(AuthenticationMethods.Password);
        GivenProof(session, TimeSpan.FromMinutes(1), AuthenticationMethods.Password);
        _tokenIssuedAt = null;

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentSignIn_DoesNotReadTheTokensTime()
    {
        // The sign-in's own window is the session's: every token of a session younger than the
        // window passes, as before this round.
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(1));
        GivenSession(session);
        _tokenIssuedAt = null;

        (await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_AnOldSignIn_WithAnOldProof_AsksToProveAgain()
    {
        var session = GivenOldSession(AuthenticationMethods.Password);
        GivenProof(session, TimeSpan.FromMinutes(16), AuthenticationMethods.Password);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_AnOldSignIn_WithNoProof_AsksToProve()
    {
        var session = GivenOldSession(AuthenticationMethods.Password);

        ShouldRequireReauthentication(await _guard.EnsureRecentSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentSignInAsync_ARecentSignIn_ReadsNoProof()
    {
        // An API that reaches a database without the proof's columns must keep every recent
        // session working: the columns are read on this one path, and only for an old session.
        var userId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(1));
        GivenSession(session);

        (await _guard.EnsureRecentSignInAsync(userId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();

        _sessionRepositoryMock.Verify(r => r.GetLastProofAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureRecentTwoFactorSignInAsync_TwoFactorsOnceAndAPasswordNow_IsNotTwoFactors()
    {
        // A stolen session and a stolen password together: the session proved two factors hours
        // ago, and this is all the thief can prove now.
        var session = GivenOldSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        GivenProof(session, TimeSpan.FromMinutes(1), AuthenticationMethods.Password);

        ShouldRequireReauthentication(await _guard.EnsureRecentTwoFactorSignInAsync(session.UserId, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureRecentTwoFactorSignInAsync_AProofOfTwoFactors_Passes()
    {
        var session = GivenOldSession(AuthenticationMethods.Password);
        GivenProof(session, TimeSpan.FromMinutes(1), AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        (await _guard.EnsureRecentTwoFactorSignInAsync(session.UserId, session.Id, CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureLiveSessionAsync_WhateverItsAge_AnswersTheUsersLiveSession()
    {
        var session = GivenOldSession(AuthenticationMethods.Password);

        var result = await _guard.EnsureLiveSessionAsync(session.UserId, session.Id, CancellationToken.None);

        result.Value.Id.Should().Be(session.Id);
        _sessionRepositoryMock.Verify(r => r.GetLastProofAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureLiveSessionAsync_NoId_SomeoneElses_OrEnded_AsksToSignIn()
    {
        var userId = Guid.NewGuid();
        var ended = Session(Guid.NewGuid(), userId, TimeSpan.FromMinutes(1), isActive: false);
        var someoneElses = Session(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(1));
        GivenSession(ended);
        GivenSession(someoneElses);

        (await _guard.EnsureLiveSessionAsync(userId, null, CancellationToken.None)).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        (await _guard.EnsureLiveSessionAsync(userId, ended.Id, CancellationToken.None)).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        (await _guard.EnsureLiveSessionAsync(userId, someoneElses.Id, CancellationToken.None)).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
    }
}
