using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Covers the concurrent session limit, exercised through
/// <see cref="LoginResponseBuilder"/> because that is the single point every
/// successful sign-in passes through — password, external provider, two-factor
/// completion, OAuth code exchange, email verification and account recovery all
/// arrive here.
///
/// The two branches are deliberately asymmetric and the tests hold them apart:
/// refusal has to happen before a token is signed, eviction has to happen after
/// the new session row exists.
/// </summary>
public class SessionLimitTests
{
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

    private readonly Mock<IUserSessionRepository> _sessionsMock = new();
    private readonly Mock<ICredentialRevocationService> _revocationMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokensMock = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptsMock = new();
    private readonly Mock<IJwtTokenService> _jwtMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly User _user = TestHelpers.CreateUser(email: "user@example.com");

    private LoginResponseBuilder CreateBuilder(SessionSettings session)
    {
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var organizations = new Mock<IOrganizationRepository>();
        organizations.Setup(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _jwtMock.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid>(), It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>()))
            .Returns("access-token");
        _jwtMock.Setup(s => s.GenerateRefreshToken()).Returns("refresh-token");
        _jwtMock.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns(Guid.NewGuid().ToString());

        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("hash");

        return new LoginResponseBuilder(
            roles.Object,
            permissions.Object,
            organizations.Object,
            _jwtMock.Object,
            keys.Object,
            _refreshTokensMock.Object,
            new Mock<IUserRepository>().Object,
            _loginAttemptsMock.Object,
            _sessionsMock.Object,
            new Mock<IIdpSessionRepository>().Object,
            new Mock<IUserKnownDeviceRepository>().Object,
            new Mock<IGeoIpLookup>().Object,
            _revocationMock.Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                Issuer = "test",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            // The new-device alert is a separate concern with its own tests; off
            // here so a device lookup cannot colour these results.
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(session),
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    private Task<ErrorOr<LoginResponse>> Sign(SessionSettings session) =>
        CreateBuilder(session).BuildAsync(
            _user, "203.0.113.10", ChromeOnWindows, deviceId: null, CancellationToken.None,
            establishIdpSession: false);

    private void GivenActiveSessionCount(int count) =>
        _sessionsMock
            .Setup(r => r.CountActiveForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    private void GivenEvicted(params UserSession[] sessions) =>
        _revocationMock
            .Setup(r => r.EnforceConcurrentSessionLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

    private void VerifyNoTokenWasMinted()
    {
        _jwtMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid>(), It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>()),
            Times.Never);
        _refreshTokensMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionsMock.Verify(
            r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- The default: the feature is present and does nothing ----

    [Fact]
    public async Task LimitOfZero_MeansUnlimitedAndCostsNothing()
    {
        // The shipped default. Neither branch may touch the database, or every
        // sign-in on every deployment pays for a limit nobody configured.
        var result = await Sign(new SessionSettings());

        result.IsError.Should().BeFalse();
        _sessionsMock.Verify(
            r => r.CountActiveForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _revocationMock.Verify(
            r => r.EnforceConcurrentSessionLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- Eviction ----

    [Fact]
    public async Task AtTheLimit_EvictsAndStillSignsTheUserIn()
    {
        GivenEvicted(TestHelpers.CreateUserSession(userId: _user.Id, deviceName: "Safari on iPhone"));

        var result = await Sign(new SessionSettings { MaxConcurrentSessions = 3 });

        result.IsError.Should().BeFalse();
        result.Value.Token!.AccessToken.Should().Be("access-token");
        _revocationMock.Verify(
            r => r.EnforceConcurrentSessionLimitAsync(
                _user.Id, 3, "session_limit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Eviction_RunsAfterTheNewSessionExists()
    {
        // Order is load-bearing: enforcing before the insert would leave the
        // account at the limit and then push it one over, and the ranking would
        // not yet see the sign-in that caused it — so the new session could be
        // the one evicted.
        var order = new List<string>();
        _sessionsMock
            .Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("create"))
            .ReturnsAsync((UserSession s, CancellationToken _) => s);
        _revocationMock
            .Setup(r => r.EnforceConcurrentSessionLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("enforce"))
            .ReturnsAsync([]);

        await Sign(new SessionSettings { MaxConcurrentSessions = 2 });

        order.Should().Equal("create", "enforce");
    }

    [Fact]
    public async Task Eviction_ReportsEverySessionItEndedInOneEvent()
    {
        // One event carrying all of them, not one event each: an operator
        // lowering the limit from twenty to five evicts fifteen at the next
        // sign-in, and fifteen emails in a second teaches people to ignore
        // security mail.
        var ended = new[]
        {
            TestHelpers.CreateUserSession(userId: _user.Id, deviceName: "Safari on iPhone"),
            TestHelpers.CreateUserSession(userId: _user.Id, deviceName: "Firefox on Ubuntu")
        };
        GivenEvicted(ended);

        await Sign(new SessionSettings { MaxConcurrentSessions = 2 });

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<SessionLimitEnforcedEvent>(e =>
                    e.UserId == _user.Id
                    && e.Limit == 2
                    && e.EndedSessions.Count == 2
                    && e.EndedSessions.Any(s => s.DeviceName == "Safari on iPhone")
                    && e.EndedSessions.Any(s => s.DeviceName == "Firefox on Ubuntu")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NothingEvicted_SaysNothing()
    {
        GivenEvicted();

        await Sign(new SessionSettings { MaxConcurrentSessions = 5 });

        _publisherMock.Verify(
            p => p.Publish(It.IsAny<SessionLimitEnforcedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvictionFailure_DoesNotCostTheUserTheirSignIn()
    {
        // The user is already authenticated by this point, and the statement
        // ends everything past the limit rather than one row, so the next
        // sign-in corrects whatever this one missed.
        _revocationMock
            .Setup(r => r.EnforceConcurrentSessionLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var result = await Sign(new SessionSettings { MaxConcurrentSessions = 2 });

        result.IsError.Should().BeFalse();
        result.Value.Token!.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task SessionInsertFailure_StillEnforcesTheLimit()
    {
        // Separate guards, deliberately: the account can already be over the
        // limit from earlier sign-ins, so a lost session row must not also mean
        // a skipped enforcement.
        _sessionsMock
            .Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        GivenEvicted();

        var result = await Sign(new SessionSettings { MaxConcurrentSessions = 2 });

        result.IsError.Should().BeFalse();
        _revocationMock.Verify(
            r => r.EnforceConcurrentSessionLimitAsync(
                _user.Id, 2, "session_limit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- Refusal ----

    [Fact]
    public async Task RefusalMode_AtTheLimit_ReturnsMaxSessionsReached()
    {
        GivenActiveSessionCount(5);

        var result = await Sign(new SessionSettings
        {
            MaxConcurrentSessions = 5,
            TerminateOldestOnMax = false
        });

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Session.MaxSessionsReached");
    }

    [Fact]
    public async Task RefusalMode_RefusesBeforeAnyCredentialExists()
    {
        // The whole reason BuildAsync returns ErrorOr. Refusing after the tokens
        // were signed and the refresh-token row written would hand out working
        // credentials for a sign-in that was rejected.
        GivenActiveSessionCount(5);

        await Sign(new SessionSettings { MaxConcurrentSessions = 5, TerminateOldestOnMax = false });

        VerifyNoTokenWasMinted();
    }

    [Fact]
    public async Task RefusalMode_RecordsTheFailedAttemptSoTheUserCanSeeWhy()
    {
        // Without this the person opens their own sign-in history and finds
        // nothing where the refusal should be.
        GivenActiveSessionCount(5);

        await Sign(new SessionSettings { MaxConcurrentSessions = 5, TerminateOldestOnMax = false });

        _loginAttemptsMock.Verify(
            r => r.CreateAsync(
                It.Is<LoginAttempt>(a =>
                    !a.IsSuccess
                    && a.UserId == _user.Id
                    && a.FailureReason == "Maximum concurrent sessions reached"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefusalMode_BelowTheLimit_SignsInNormally()
    {
        GivenActiveSessionCount(4);

        var result = await Sign(new SessionSettings
        {
            MaxConcurrentSessions = 5,
            TerminateOldestOnMax = false
        });

        result.IsError.Should().BeFalse();
        result.Value.Token!.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task RefusalMode_NeverEvicts()
    {
        GivenActiveSessionCount(1);

        await Sign(new SessionSettings { MaxConcurrentSessions = 5, TerminateOldestOnMax = false });

        _revocationMock.Verify(
            r => r.EnforceConcurrentSessionLimitAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvictionMode_NeverCounts()
    {
        // The counting query exists only for the refusal branch; eviction learns
        // what it needs from the statement that does the work.
        GivenEvicted();

        await Sign(new SessionSettings { MaxConcurrentSessions = 5 });

        _sessionsMock.Verify(
            r => r.CountActiveForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
