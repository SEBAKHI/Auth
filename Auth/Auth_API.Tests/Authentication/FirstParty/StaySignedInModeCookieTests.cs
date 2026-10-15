using System.Net;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// OI-165 through the HTTP pipeline, read off the <c>Set-Cookie</c> headers a
/// browser receives: the administrator's <c>StaySignedInMode</c> decides whether
/// the IdP session cookie and the app's refresh cookie outlive the browser. The
/// mediator answers through the REAL <see cref="LoginResponseBuilder"/>, given the
/// command's choice exactly as the handlers pass it (<c>StaySignedInExitCoverageTests</c>
/// pins those arguments), so the chain from the request body to the header runs
/// through the one place the setting is applied. A change of the setting touches
/// no session already minted (D-165-1 (b)): the refresh keeps deciding from the
/// session it presents.
/// </summary>
public class StaySignedInModeCookieTests
{
    private const string LoginPath = "/api/v1/auth/login";
    private const int SessionLifetimeSeconds = 7 * 24 * 60 * 60;
    private static readonly string ConsoleCookie = RefreshCookieOf(ConsoleApp);

    private static Task<FirstPartyHost> StartAsync(StaySignedInPolicy mode) =>
        FirstPartyHost.StartAsync([ConsoleApp], cookieEnabled: true,
            configureIdp: settings => settings.StaySignedInMode = mode.ToString());

    /// <summary>A real builder over in-memory collaborators, under <paramref name="mode"/>.</summary>
    private static LoginResponseBuilder Builder(StaySignedInPolicy mode)
    {
        var sessions = new Mock<IUserSessionRepository>();
        sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));
        var idpSessions = new Mock<IIdpSessionRepository>();
        idpSessions.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession row, CancellationToken _) => row);
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity token, CancellationToken _) => token);
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(value => "hash:" + value);
        var claims = new Mock<ITokenClaimsResolver>();
        claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        var generated = 0;
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "generated-" + ++generated);
        jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");

        return new LoginResponseBuilder(
            claims.Object, TestHelpers.CreatePlatformMfaPolicy(), jwt.Object, keys.Object, refreshTokens.Object,
            new Mock<IAccountLockout>().Object, new Mock<ILoginAttemptRepository>().Object, sessions.Object,
            idpSessions.Object, new Mock<IUserKnownDeviceRepository>().Object, new Mock<IGeoIpLookup>().Object,
            new Mock<ICredentialRevocationService>().Object, new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings { AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7 }),
            TestHelpers.CreateOptions(new IdentityProviderSettings { StaySignedInMode = mode.ToString() }),
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(new SessionSettings()),
            TimeProvider.System,
            TestHelpers.BrowserSignIn(),
            NullLogger<LoginResponseBuilder>.Instance);
    }

    /// <summary>The password sign-in answers as its handler does: the body's choice, through the builder.</summary>
    private static void AnswerLoginThroughTheBuilder(FirstPartyHost host, StaySignedInPolicy mode) =>
        host.Sender
            .Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<LoginResponse>> request, CancellationToken cancellationToken) =>
                Builder(mode).BuildAsync(
                    TestHelpers.CreateUser(twoFactorEnabled: false), null, null, null, AuthenticationMethods.Password,
                    passwordProven: true, cancellationToken, persistent: ((LoginCommand)request).StaySignedIn));

    /// <summary>The e-mail verification sign-in answers as its handler does: no choice passed.</summary>
    private static void AnswerVerifyEmailThroughTheBuilder(FirstPartyHost host, StaySignedInPolicy mode) =>
        host.Sender
            .Setup(s => s.Send(It.IsAny<VerifyEmailCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (IRequest<ErrorOr<VerifyEmailResult>> _, CancellationToken cancellationToken) =>
            {
                var login = await Builder(mode).BuildAsync(
                    TestHelpers.CreateUser(twoFactorEnabled: false), null, null, null, AuthenticationMethods.EmailCode,
                    passwordProven: false, cancellationToken);
                return (ErrorOr<VerifyEmailResult>)new VerifyEmailResult(login.Value);
            });

    private static string? Header(HttpResponseMessage response, string name) =>
        SetCookies(response).SingleOrDefault(header => header.StartsWith(name + "=", StringComparison.Ordinal));

    private static void ShouldBeABrowserSessionCookie(string? header, string because)
    {
        header.Should().NotBeNull(because);
        header!.ToLowerInvariant().Should().NotContain("max-age", because)
            .And.NotContain("expires", because);
    }

    private static void ShouldLast(string? header, int seconds, string because)
    {
        header.Should().NotBeNull(because);
        header!.ToLowerInvariant().Should().Contain($"max-age={seconds}", because);
    }

    private static Task<HttpResponseMessage> SignInAsync(FirstPartyHost host, bool staySignedIn) =>
        host.PostAsync(
            LoginPath, $$"""{"email":"a@b.c","password":"x","staySignedIn":{{(staySignedIn ? "true" : "false")}}}""",
            origin: ConsoleApp);

    private static Task<HttpResponseMessage> VerifyEmailAsync(FirstPartyHost host) =>
        host.PostAsync("/api/v1/auth/verify-email", """{"email":"a@b.c","otp":"123456"}""", origin: ConsoleApp);

    // --- sign-in -------------------------------------------------------------

    [Fact]
    public async Task AlwaysBrowserSession_ATickedSignIn_IsAcceptedAsABrowserSession()
    {
        // An old sign-in page still open in a tab sends the tick: it is ignored,
        // never refused, so the sign-in does not fail.
        await using var host = await StartAsync(StaySignedInPolicy.AlwaysBrowserSession);
        AnswerLoginThroughTheBuilder(host, StaySignedInPolicy.AlwaysBrowserSession);

        var response = await SignInAsync(host, staySignedIn: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, "auth_idp"), "the administrator decided: always a browser session");
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "and so is the app's own sign-in");
    }

    [Fact]
    public async Task AlwaysStaySignedIn_AnUntickedSignIn_OutlivesTheBrowser()
    {
        await using var host = await StartAsync(StaySignedInPolicy.AlwaysStaySignedIn);
        AnswerLoginThroughTheBuilder(host, StaySignedInPolicy.AlwaysStaySignedIn);

        var response = await SignInAsync(host, staySignedIn: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldLast(Header(response, "auth_idp"), SessionLifetimeSeconds, "the IdP session cookie lasts IdpSessionLifetimeDays");
        ShouldLast(Header(response, ConsoleCookie), SessionLifetimeSeconds, "and the app's refresh cookie its token's lifetime");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonChooses_TheTickDecides_AsBefore(bool staySignedIn)
    {
        await using var host = await StartAsync(StaySignedInPolicy.PersonChooses);
        AnswerLoginThroughTheBuilder(host, StaySignedInPolicy.PersonChooses);

        var response = await SignInAsync(host, staySignedIn);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (staySignedIn)
        {
            ShouldLast(Header(response, "auth_idp"), SessionLifetimeSeconds, "ticked");
            ShouldLast(Header(response, ConsoleCookie), SessionLifetimeSeconds, "ticked");
        }
        else
        {
            ShouldBeABrowserSessionCookie(Header(response, "auth_idp"), "unticked");
            ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "unticked");
        }
    }

    [Fact]
    public async Task AlwaysStaySignedIn_TheEmailVerificationSignIn_OutlivesTheBrowser()
    {
        // An exit that passes no choice follows the administrator too: the setting
        // is applied in the builder, not in the three handlers that carry the tick.
        await using var host = await StartAsync(StaySignedInPolicy.AlwaysStaySignedIn);
        AnswerVerifyEmailThroughTheBuilder(host, StaySignedInPolicy.AlwaysStaySignedIn);

        var response = await VerifyEmailAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldLast(Header(response, "auth_idp"), SessionLifetimeSeconds, "every sign-in stays signed in");
        ShouldLast(Header(response, ConsoleCookie), SessionLifetimeSeconds, "every sign-in stays signed in");
    }

    [Fact]
    public async Task PersonChooses_TheEmailVerificationSignIn_IsABrowserSession()
    {
        await using var host = await StartAsync(StaySignedInPolicy.PersonChooses);
        AnswerVerifyEmailThroughTheBuilder(host, StaySignedInPolicy.PersonChooses);

        var response = await VerifyEmailAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, "auth_idp"), "an exit with no box mints a browser session, as today");
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "an exit with no box mints a browser session, as today");
    }

    // --- refresh: existing sessions keep what they were given ------------------

    private static async Task<HttpResponseMessage> RefreshPresentingAsync(FirstPartyHost host, bool persistent)
    {
        var user = Guid.NewGuid();
        StaySignedInCookieTests.PresentsSession(host, "sso-token", persistent, user);
        host.Sender
            .Setup(s => s.Send(It.IsAny<Auth.Application.Features.Authentication.RefreshToken.RefreshTokenCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<TokenResponse>)Tokens("rotated-token", refreshExpiresIn: 1234, userId: user));

        return await host.PostAsync(
            "/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token", "auth_idp=sso-token"]);
    }

    [Fact]
    public async Task AlwaysBrowserSession_ARefreshPresentingASessionTickedBefore_KeepsTheCookiesLifetime()
    {
        // D-165-1 (b): the person ticked before the administrator changed the
        // setting. Their session stays persistent, and so does the cookie its
        // refreshes write, until the session ends.
        await using var host = await StartAsync(StaySignedInPolicy.AlwaysBrowserSession);

        var response = await RefreshPresentingAsync(host, persistent: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldLast(Header(response, ConsoleCookie), 1234, "the refresh follows the presented session, not the setting");
    }

    [Fact]
    public async Task AlwaysStaySignedIn_ARefreshPresentingABrowserSession_StaysABrowserSessionCookie()
    {
        await using var host = await StartAsync(StaySignedInPolicy.AlwaysStaySignedIn);

        var response = await RefreshPresentingAsync(host, persistent: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "the presented session ends with the browser");
    }
}
