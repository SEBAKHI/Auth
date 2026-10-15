using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-154 at the builder every sign-in exit goes through: "Stay signed in" decides
/// the IdP session's kind and lifetime and the flag the HTTP layer writes the
/// cookies from; absent, it is a browser session (fail closed); and the sign-in's
/// refresh token names the IdP session it was born with (D-154-2 (b)).
/// </summary>
public class LoginResponseBuilderStaySignedInTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly User _user = TestHelpers.CreateUser(twoFactorEnabled: false);
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly List<IdpSession> _minted = [];
    private readonly List<RefreshTokenEntity> _tokens = [];
    private int _generated;

    public LoginResponseBuilderStaySignedInTests()
    {
        _sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));
        _idpSessions.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .Callback((IdpSession row, CancellationToken _) => _minted.Add(row))
            .ReturnsAsync((IdpSession row, CancellationToken _) => row);
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity token, CancellationToken _) => _tokens.Add(token))
            .ReturnsAsync((RefreshTokenEntity token, CancellationToken _) => token);
        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "generated-" + ++_generated);
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");
    }

    private LoginResponseBuilder Builder(
        IdentityProviderSettings? settings = null, Auth.Application.Interfaces.IBrowserSignInContext? browserSignIn = null)
    {
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(value => "hash:" + value);
        var claims = new Mock<ITokenClaimsResolver>();
        claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));

        return new LoginResponseBuilder(
            claims.Object,
            TestHelpers.CreatePlatformMfaPolicy(),
            _jwt.Object,
            keys.Object,
            _refreshTokens.Object,
            new Mock<IAccountLockout>().Object,
            new Mock<ILoginAttemptRepository>().Object,
            _sessions.Object,
            _idpSessions.Object,
            new Mock<IUserKnownDeviceRepository>().Object,
            new Mock<IGeoIpLookup>().Object,
            new Mock<ICredentialRevocationService>().Object,
            new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings { AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7 }),
            TestHelpers.CreateOptions(settings ?? new IdentityProviderSettings()),
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(new SessionSettings()),
            new FixedTimeProvider(Now),
            browserSignIn ?? TestHelpers.BrowserSignIn(),
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    [Fact]
    public async Task StaySignedIn_MintsAPersistentSession_ForTheSessionLifetime()
    {
        var response = (await Builder().BuildAsync(
            _user, "203.0.113.10", "agent", null, AuthenticationMethods.Password, passwordProven: true,
            CancellationToken.None, persistent: true)).Value;

        var session = _minted.Should().ContainSingle().Subject;
        session.IsPersistent.Should().BeTrue();
        session.CreatedAt.Should().Be(Now.UtcDateTime);
        session.ExpiresAt.Should().Be(Now.UtcDateTime.AddDays(7));
        response.IdpSessionIsPersistent.Should().BeTrue();
        ((ISessionIssuingResponse)response).IssuedSessionIsPersistent.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutTheArgument_MintsABrowserSession_ForTheBrowserSessionMaximum()
    {
        // The default is false, so an exit that forgets to pass the choice fails closed.
        var response = (await Builder().BuildAsync(
            _user, "203.0.113.10", "agent", null, AuthenticationMethods.EmailCode, passwordProven: false,
            CancellationToken.None)).Value;

        var session = _minted.Should().ContainSingle().Subject;
        session.IsPersistent.Should().BeFalse();
        session.ExpiresAt.Should().Be(Now.UtcDateTime.AddHours(10));
        response.IdpSessionIsPersistent.Should().BeFalse();
        ((ISessionIssuingResponse)response).IssuedSessionIsPersistent.Should().BeFalse();
    }

    [Fact]
    public async Task ABrowserSession_NeverOutlivesTheStaySignedInLifetime()
    {
        await Builder(new IdentityProviderSettings { IdpSessionLifetimeDays = 1, IdpBrowserSessionLifetimeHours = 24 })
            .BuildAsync(_user, null, null, null, AuthenticationMethods.Password, passwordProven: true,
                CancellationToken.None, persistent: false);

        _minted.Should().ContainSingle().Which.ExpiresAt.Should().Be(Now.UtcDateTime.AddDays(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSignInsRefreshToken_NamesTheIdpSessionMintedWithIt(bool persistent)
    {
        await Builder().BuildAsync(
            _user, null, null, null, AuthenticationMethods.Password, passwordProven: true,
            CancellationToken.None, persistent: persistent);

        _tokens.Should().ContainSingle().Which.IdpSessionId.Should().Be(_minted.Single().Id);
    }

    [Fact]
    public async Task ASignInWithoutABrowserOrigin_MintsTheSession_ButLeavesTheTokenUnlinked()
    {
        // D-74-3 (a): a server, the SDK or a script keeps the refresh behaviour it
        // always had. The session is still minted (its cookie goes nowhere useful),
        // but nothing ties the token to it.
        var response = (await Builder(browserSignIn: TestHelpers.BrowserSignIn(fromBrowser: false)).BuildAsync(
            _user, null, null, null, AuthenticationMethods.Password, passwordProven: true, CancellationToken.None)).Value;

        _minted.Should().ContainSingle();
        _tokens.Should().ContainSingle().Which.IdpSessionId.Should().BeNull();
        response.IdpSessionId.Should().Be(_minted.Single().Id,
            "a same-person re-sign-in in a browser still finds the session to replace");
    }

    [Fact]
    public async Task TheIdpSession_IsMintedBeforeTheRefreshToken_AndTheResponseNamesIt()
    {
        // OI-154 review F2: a link names only a session that exists, so a link
        // naming no row means a purged session, never a failed minting.
        var order = new List<string>();
        _idpSessions.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .Callback((IdpSession row, CancellationToken _) => { order.Add("idp session"); _minted.Add(row); })
            .ReturnsAsync((IdpSession row, CancellationToken _) => row);
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity token, CancellationToken _) => { order.Add("refresh token"); _tokens.Add(token); })
            .ReturnsAsync((RefreshTokenEntity token, CancellationToken _) => token);

        var response = (await Builder().BuildAsync(
            _user, null, null, null, AuthenticationMethods.Password, passwordProven: true, CancellationToken.None)).Value;

        order.Should().Equal("idp session", "refresh token");
        response.IdpSessionId.Should().Be(_minted.Single().Id);
        ((ISessionIssuingResponse)response).IssuedIdpSessionId.Should().Be(_minted.Single().Id,
            "the sign-in filter hands the person's previous chains to it");
    }

    [Fact]
    public async Task TheCodeExchange_MintsNoIdpSession_AndLinksTheTokenToTheCodesOne()
    {
        var linked = Guid.NewGuid();

        var response = (await Builder().BuildAsync(
            _user, null, null, null, AuthenticationMethods.Unknown, passwordProven: false, CancellationToken.None,
            establishIdpSession: false, audience: "CRM", applicationId: Guid.NewGuid(), scope: "openid",
            linkedIdpSessionId: linked)).Value;

        _minted.Should().BeEmpty();
        _tokens.Should().ContainSingle().Which.IdpSessionId.Should().Be(linked);
        response.IdpSessionToken.Should().BeNull();
        response.IdpSessionIsPersistent.Should().BeFalse();
    }

    /// <summary>
    /// OI-165: the administrator's StaySignedInMode is applied here, at the one point
    /// every sign-in session is minted: the session's kind and lifetime and the flag
    /// the cookies are written from all follow the resolved value, whatever the
    /// exit passed.
    /// </summary>
    [Theory]
    [InlineData(StaySignedInMode.PersonChooses, false, false)]
    [InlineData(StaySignedInMode.PersonChooses, true, true)]
    [InlineData(StaySignedInMode.AlwaysBrowserSession, false, false)]
    [InlineData(StaySignedInMode.AlwaysBrowserSession, true, false)]
    [InlineData(StaySignedInMode.AlwaysStaySignedIn, false, true)]
    [InlineData(StaySignedInMode.AlwaysStaySignedIn, true, true)]
    public async Task TheMode_DecidesTheMintedSessionAndTheCookieFlag(
        StaySignedInMode mode, bool persistent, bool expected)
    {
        var settings = new IdentityProviderSettings { StaySignedInMode = mode };

        var response = (await Builder(settings).BuildAsync(
            _user, null, null, null, AuthenticationMethods.Password, passwordProven: true,
            CancellationToken.None, persistent: persistent)).Value;

        var session = _minted.Should().ContainSingle().Subject;
        session.IsPersistent.Should().Be(expected);
        session.ExpiresAt.Should().Be(Now.UtcDateTime.Add(settings.IdpSessionLifetimeFor(expected)));
        response.IdpSessionIsPersistent.Should().Be(expected);
        ((ISessionIssuingResponse)response).IssuedSessionIsPersistent.Should().Be(expected);
    }

    [Fact]
    public async Task UnderAlwaysStaySignedIn_AnExitThatPassesNothing_MintsAPersistentSession()
    {
        // Registration completion, the e-mail verification sign-in and account
        // recovery pass no choice: under the default they fail closed, under
        // "always stay signed in" they follow the administrator.
        var response = (await Builder(new IdentityProviderSettings { StaySignedInMode = StaySignedInMode.AlwaysStaySignedIn })
            .BuildAsync(_user, null, null, null, AuthenticationMethods.EmailCode, passwordProven: false,
                CancellationToken.None)).Value;

        _minted.Should().ContainSingle().Which.IsPersistent.Should().BeTrue();
        response.IdpSessionIsPersistent.Should().BeTrue();
    }

    [Fact]
    public async Task UnderAlwaysStaySignedIn_TheCodeExchange_StillMintsNothingPersistent()
    {
        var response = (await Builder(new IdentityProviderSettings { StaySignedInMode = StaySignedInMode.AlwaysStaySignedIn })
            .BuildAsync(_user, null, null, null, AuthenticationMethods.Unknown, passwordProven: false, CancellationToken.None,
                establishIdpSession: false, audience: "CRM", applicationId: Guid.NewGuid(), scope: "openid",
                linkedIdpSessionId: Guid.NewGuid())).Value;

        _minted.Should().BeEmpty();
        response.IdpSessionIsPersistent.Should().BeFalse();
        ((ISessionIssuingResponse)response).IssuedSessionIsPersistent.Should().BeFalse();
    }

    [Fact]
    public async Task AMintingThatFails_StillSignsIn_AsABrowserSession()
    {
        // Best effort, as before: the sign-in succeeds without SSO, and nothing it
        // writes can make a cookie outlive the browser.
        _idpSessions.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await Builder().BuildAsync(
            _user, null, null, null, AuthenticationMethods.Password, passwordProven: true,
            CancellationToken.None, persistent: true);

        result.IsError.Should().BeFalse();
        result.Value.IdpSessionToken.Should().BeNull();
        result.Value.IdpSessionIsPersistent.Should().BeFalse();
        ((ISessionIssuingResponse)result.Value).IssuedSessionIsPersistent.Should().BeFalse();
        _tokens.Should().ContainSingle().Which.IdpSessionId.Should().BeNull(
            "a session that was never minted is not named: the platform app's sign-in refreshes as an unlinked one");
        result.Value.IdpSessionId.Should().BeNull();
    }
}
