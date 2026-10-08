using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S08 T5 (M5): a refresh makes the platform-administrator decision again, from the
/// session row read BEFORE the mint. A missing row or a failed read is Unknown and a
/// warning, never an error to the client; a factor removed since the sign-in
/// withholds the authority; the factor is read from the two-factor row; and an
/// application's refresh token is left out of it entirely.
/// </summary>
public class RefreshTokenMfaPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTime StartedAt = new(2026, 10, 8, 8, 0, 0, DateTimeKind.Unspecified);

    private static readonly TokenClaims AdminClaims = new(["super-admin"], ["*"], []);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _logger = new();
    private readonly List<string> _calls = [];

    private (IEnumerable<string> Permissions, IEnumerable<string> Roles, AccessTokenAuthentication Authentication)? _minted;

    public RefreshTokenMfaPolicyTests()
    {
        _users.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: UserId));
        _claims.Setup(r => r.ResolveAsync(UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminClaims);
        _keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(value => "hash:" + value);
        _refreshTokens.Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Callback((User _, IEnumerable<string> permissions, IEnumerable<string> roles, AccessTokenAuthentication authentication,
                Guid? _, IEnumerable<(Guid, string)>? _, string? _, string? _, TokenOrganization? _) =>
            {
                _calls.Add("mint");
                _minted = (permissions.ToList(), roles.ToList(), authentication);
            })
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns("new-refresh");
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");
    }

    private RefreshTokenCommandHandler Handler(IPlatformMfaPolicy policy) => new(
        _users.Object,
        _refreshTokens.Object,
        _claims.Object,
        policy,
        _applications.Object,
        _access.Object,
        _jwt.Object,
        _keys.Object,
        _sessions.Object,
        new Mock<ICredentialRevocationService>().Object,
        new Mock<IPublisher>().Object,
        TestHelpers.CreateOptions(new JwtSettings
        {
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7,
            RotateRefreshTokens = true
        }),
        _logger.Object);

    private void GivenToken(Guid? applicationId = null, Guid? sessionId = null)
    {
        var token = TestHelpers.CreateRefreshToken(
            userId: UserId, sessionId: sessionId ?? SessionId, applicationId: applicationId,
            expiresAt: DateTime.UtcNow.AddDays(7));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync("hash:presented", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
    }

    private void GivenSession(AuthenticationMethods methods, bool isActive = true)
    {
        var session = TestHelpers.CreateUserSession(
            id: SessionId, userId: UserId, applicationId: null, createdAt: StartedAt,
            expiresAt: DateTime.UtcNow.AddDays(7), isActive: isActive,
            terminatedAt: isActive ? null : DateTime.UtcNow, methods: methods);
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("session-read"))
            .ReturnsAsync(session);
    }

    private Task<ErrorOr.ErrorOr<Auth.Application.DTOs.TokenResponse>> Refresh(IPlatformMfaPolicy policy) =>
        Handler(policy).Handle(new RefreshTokenCommand("presented", "127.0.0.1", "agent"), CancellationToken.None);

    private void VerifyWarning(string text, Times times) =>
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(text)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task Handle_ReadsTheSessionRow_BeforeTheMint()
    {
        GivenToken();
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        var result = await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));

        result.IsError.Should().BeFalse();
        _calls.Should().Equal(["session-read", "mint"],
            "what the session proved decides the token, so the row is read before the token exists");
        _sessions.Verify(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()), Times.Once,
            "the same row serves the activity update; it is not read twice");
    }

    [Fact]
    public async Task Handle_MfaSession_KeepsPlatformAuthority_WithAmrAndAuthTime()
    {
        GivenToken();
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));

        _minted!.Value.Permissions.Should().Equal("*");
        _minted.Value.Authentication.Requirement.Should().Be(MfaRequirement.None);
        _minted.Value.Authentication.Methods.Should().Be(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        _minted.Value.Authentication.AuthTime.Should().Be(new DateTimeOffset(StartedAt, TimeSpan.Zero),
            "auth_time is the session's start, read back as UTC though Dapper hands it over with no Kind");
    }

    [Fact]
    public async Task Handle_FactorRemovedSinceTheSignIn_Enrols()
    {
        // The session still says it proved two factors; the factor row is gone.
        GivenToken();
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: false));

        _minted!.Value.Permissions.Should().BeEmpty();
        _minted.Value.Roles.Should().BeEmpty();
        _minted.Value.Authentication.Requirement.Should().Be(MfaRequirement.Enroll);
    }

    [Fact]
    public async Task Handle_MissingSessionRow_IsUnknown_AndAWarning_NotAnError()
    {
        GivenToken();
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync((UserSession?)null);

        var result = await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));

        result.IsError.Should().BeFalse("a missing session row is never an error to the client");
        _minted!.Value.Authentication.Methods.IsUnknown.Should().BeTrue();
        _minted.Value.Authentication.AuthTime.Should().BeNull();
        _minted.Value.Authentication.Requirement.Should().Be(MfaRequirement.Reauthenticate);
        _minted.Value.Permissions.Should().BeEmpty();
        VerifyWarning("authentication methods are unknown", Times.Once());
    }

    [Fact]
    public async Task Handle_SessionReadThrows_IsUnknown_AndAWarning_NotAnError()
    {
        GivenToken();
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));

        result.IsError.Should().BeFalse();
        _minted!.Value.Authentication.Methods.IsUnknown.Should().BeTrue();
        _minted.Value.Authentication.Requirement.Should().Be(MfaRequirement.Reauthenticate);
        VerifyWarning("Failed to read session", Times.Once());
    }

    [Fact]
    public async Task Handle_EndedSession_IsUnknown()
    {
        GivenToken();
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp), isActive: false);

        await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));

        _minted!.Value.Authentication.Methods.IsUnknown.Should().BeTrue();
        _minted.Value.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_LegacySession_ReauthenticatesUnderEnforcement_AndChangesNothingWithTheSwitchOff()
    {
        GivenToken();
        GivenSession(AuthenticationMethods.Unknown);

        await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true));
        _minted!.Value.Authentication.Requirement.Should().Be(MfaRequirement.Reauthenticate);
        _minted.Value.Permissions.Should().BeEmpty();

        GivenToken(); // the first refresh rotated the token it was given
        await Refresh(TestHelpers.CreatePlatformMfaPolicy(enforce: false, hasEnabledFactor: true));
        _minted!.Value.Authentication.Requirement.Should().Be(MfaRequirement.None);
        _minted.Value.Permissions.Should().Equal(["*"], "with the switch off a refresh mints today's claims");
        _minted.Value.Authentication.AuthTime.Should().BeNull("Unknown claims no authentication time");
    }

    [Fact]
    public async Task Handle_ApplicationRefreshToken_SkipsThePolicy_AndClaimsNoAuthentication()
    {
        var applicationId = Guid.NewGuid();
        GivenToken(applicationId: applicationId);
        GivenSession(AuthenticationMethods.Password);
        _applications.Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, isActive: true));
        _claims.Setup(r => r.ResolveAsync(UserId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims(["exhibitor"], ["edis:exhibitors:read"], []));

        var store = new Mock<ITwoFactorStateStore>(MockBehavior.Strict);
        var policy = new Auth.Application.Features.Authentication.Common.PlatformMfaPolicy(
            store.Object, Mock.Of<ITokenClaimsResolver>(),
            TestHelpers.CreateOptions(new TwoFactorSettings { EnforceForPlatformAdmins = true }),
            Mock.Of<ILogger<Auth.Application.Features.Authentication.Common.PlatformMfaPolicy>>());

        var result = await Refresh(policy);

        result.IsError.Should().BeFalse();
        _minted!.Value.Permissions.Should().Equal("edis:exhibitors:read");
        _minted.Value.Authentication.Should().Be(AccessTokenAuthentication.Unrecorded);
        store.VerifyNoOtherCalls();
    }
}
