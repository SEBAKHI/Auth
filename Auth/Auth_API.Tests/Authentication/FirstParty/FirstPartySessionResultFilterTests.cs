using System.Net;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.CompleteRegistration;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Domain.Errors;
using ErrorOr;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// How a session's credentials leave the API, per request, through the real sign-in
/// and refresh actions: the SSO cookie always, the refresh token in an HttpOnly
/// cookie for a listed app while the delivery is on, in the body otherwise.
/// </summary>
public class FirstPartySessionResultFilterTests
{
    private static readonly string ConsoleCookie = RefreshCookieOf(ConsoleApp);

    private static void ArrangeLogin(FirstPartyHost host, LoginResponse response) =>
        host.Sender.Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<LoginResponse>)response);

    private static void ArrangeRefresh(FirstPartyHost host, ErrorOr<TokenResponse> response) =>
        host.Sender.Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private static string? CookieHeader(HttpResponseMessage response, string name) =>
        SetCookies(response).SingleOrDefault(header => header.StartsWith(name + "=", StringComparison.Ordinal));

    private const string LoginBody = """{"email":"a@b.c","password":"x"}""";

    [Fact]
    public async Task CookieDeliveryOn_ListedApp_MovesTheRefreshTokenIntoAStrictHostOnlyCookie()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: true);
        ArrangeLogin(host, SignIn("real-refresh-token"));

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = CookieHeader(response, ConsoleCookie);
        cookie.Should().NotBeNull();
        cookie!.ToLowerInvariant().Should()
            .StartWith($"{ConsoleCookie.ToLowerInvariant()}=real-refresh-token;")
            .And.Contain("max-age=604800")
            .And.Contain("path=/")
            .And.Contain("secure")
            .And.Contain("samesite=strict")
            .And.Contain("httponly")
            .And.NotContain("domain=");

        var body = await JsonAsync(response);
        body.GetProperty("token").GetProperty("refreshToken").GetString().Should().Be("__cookie__");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("real-refresh-token");
        CookieHeader(response, "auth_idp").Should().NotBeNull("the SSO cookie is set in the same place");
    }

    [Fact]
    public async Task CookieDeliveryOn_ARefreshByCookie_ReissuesTheCookieForTheTokensOwnLifetime()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeRefresh(host, Tokens("rotated-token", refreshExpiresIn: 1234));

        var response = await host.PostAsync(
            "/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieHeader(response, ConsoleCookie)!.ToLowerInvariant().Should()
            .StartWith($"{ConsoleCookie.ToLowerInvariant()}=rotated-token;")
            .And.Contain("max-age=1234");
        (await JsonAsync(response)).GetProperty("refreshToken").GetString().Should().Be("__cookie__");
    }

    [Fact]
    public async Task CookieDeliveryOn_ARealBodyTokenFromAListedApp_MigratesIntoTheCookie_AndIsCounted()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeRefresh(host, Tokens("rotated-token"));

        var response = await host.PostAsync(
            "/api/v1/auth/refresh", """{"refreshToken":"stored-legacy-token"}""", origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieHeader(response, ConsoleCookie).Should().StartWith($"{ConsoleCookie}=rotated-token;");
        (await JsonAsync(response)).GetProperty("refreshToken").GetString().Should().Be("__cookie__");
        host.Warnings.Should().Contain(message => message.StartsWith("SpaRefresh.LegacyBodyMigrated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CookieDeliveryOff_TokenFromTheCookie_ReturnsItInTheBody_AndExpiresTheCookie()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        ArrangeRefresh(host, Tokens("rotated-token"));

        var response = await host.PostAsync(
            "/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("refreshToken").GetString().Should().Be("rotated-token");
        CookieHeader(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=0");
    }

    [Fact]
    public async Task UnlistedOrigin_LeavesTheBodyByteForByte_AsWithoutTheFeature()
    {
        // Refresh, not login: login refuses an unlisted browser Origin outright.
        await using var on = await StartAsync([ConsoleApp], cookieEnabled: true);
        await using var off = await StartAsync([], cookieEnabled: false);
        ArrangeRefresh(on, Tokens("rotated-token"));
        ArrangeRefresh(off, Tokens("rotated-token"));
        const string body = """{"refreshToken":"partner-token"}""";

        var withFeature = await on.PostAsync("/api/v1/auth/refresh", body, origin: "https://partner.example.org");
        var without = await off.PostAsync("/api/v1/auth/refresh", body);

        withFeature.StatusCode.Should().Be(HttpStatusCode.OK);

        (await withFeature.Content.ReadAsStringAsync()).Should().Be(await without.Content.ReadAsStringAsync());
        SetCookies(withFeature).Should().NotContain(header => header.StartsWith("__Host-", StringComparison.Ordinal));
    }

    /// <summary>
    /// OI-78: the cookie switch ships on, and the origin list ships empty. Until the
    /// operator lists the apps no origin matches, so every sign-in keeps the
    /// refresh token in the body and no refresh cookie is set — the switch is
    /// inert, not half-on.
    /// </summary>
    [Theory]
    [InlineData(ConsoleApp)]
    [InlineData(AccountsApp)]
    [InlineData(Sibling)]
    [InlineData(null)]
    public async Task ShippedDefaults_WithAnEmptyList_SignInFromAnyOrigin_KeepsTheRefreshTokenInTheBody(string? origin)
    {
        new Auth.Application.Configuration.IdentityProviderSettings().SpaRefreshCookieEnabled
            .Should().BeTrue("this test is about the shipped default, which is on");
        await using var host = await StartAsync([], cookieEnabled: null);
        ArrangeLogin(host, SignIn("real-refresh-token"));

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("token").GetProperty("refreshToken").GetString()
            .Should().Be("real-refresh-token");
        SetCookies(response).Should().NotContain(header => header.StartsWith("__Host-", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of the shipped default: once the operator lists the apps, the
    /// cookie applies with no second switch to turn on.
    /// </summary>
    [Fact]
    public async Task ShippedDefaults_WithTheAppsListed_DeliversTheRefreshTokenInTheCookie()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: null);
        ArrangeLogin(host, SignIn("real-refresh-token"));

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieHeader(response, ConsoleCookie).Should().StartWith($"{ConsoleCookie}=real-refresh-token;");
        (await JsonAsync(response)).GetProperty("token").GetProperty("refreshToken").GetString().Should().Be("__cookie__");
    }

    [Fact]
    public async Task NoOrigin_ServerClient_KeepsTheBodyToken()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeRefresh(host, Tokens("rotated-token"));

        var response = await host.PostAsync("/api/v1/auth/refresh", """{"refreshToken":"sdk-token"}""");

        (await JsonAsync(response)).GetProperty("refreshToken").GetString().Should().Be("rotated-token");
        SetCookies(response).Should().BeEmpty();
    }

    [Fact]
    public async Task OAuthTokenEndpoint_KeepsTheBodyContract_AndSetsNoCookie_EvenForAListedOrigin()
    {
        // Statement 12: /auth/token is the public-client contract. It returns the
        // real refresh token in the body, whatever the Origin and the switch.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeRefresh(host, Tokens("oauth-rotated"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = "oauth-token",
            }),
        };
        request.Headers.TryAddWithoutValidation("Origin", ConsoleApp);

        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("oauth-rotated").And.NotContain("__cookie__");
        SetCookies(response).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoFactorChallenge_SetsNoCookieAtAll()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeLogin(host, new LoginResponse { RequiresTwoFactor = true, TwoFactorChallengeToken = "challenge" });

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        SetCookies(response).Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyEmail_AdminPath204_SetsNothing()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        host.Sender.Setup(s => s.Send(It.IsAny<VerifyEmailCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<VerifyEmailResult>)new VerifyEmailResult(null));

        var response = await host.PostAsync(
            "/api/v1/auth/verify-email", $$"""{"userId":"{{Guid.NewGuid()}}","otp":"123456"}""", origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookies(response).Should().BeEmpty();
    }

    public static TheoryData<string, string, Action<FirstPartyHost>> SignInExits => new()
    {
        { "/api/v1/auth/login", LoginBody,
            h => h.Sender.Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
        { "/api/v1/auth/external-login", """{"provider":"google","idToken":"x"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<ExternalLoginCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
        { "/api/v1/auth/registration/complete", """{"pendingId":"p","otp":"123456","password":"x","firstName":"a","lastName":"b"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<CompleteRegistrationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
        { "/api/v1/auth/verify-email", """{"email":"a@b.c","otp":"123456"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<VerifyEmailCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<VerifyEmailResult>)new VerifyEmailResult(SignIn())) },
        { "/api/v1/auth/deletion/recover", """{"email":"a@b.c","password":"x"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<RecoverAccountCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
        { "/api/v1/auth/deletion/recover-external", """{"provider":"google","idToken":"x"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<RecoverAccountExternalCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
        { "/api/v1/auth/2fa/verify", """{"challengeToken":"c","code":"123456"}""",
            h => h.Sender.Setup(s => s.Send(It.IsAny<VerifyTwoFactorLoginCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((ErrorOr<LoginResponse>)SignIn()) },
    };

    [Theory]
    [MemberData(nameof(SignInExits))]
    public async Task EverySignInExit_SetsTheRefreshCookieAndTheSsoCookie_IncludingTheRecoveries(
        string path, string body, Action<FirstPartyHost> arrange)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        arrange(host);

        var response = await host.PostAsync(path, body, origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        CookieHeader(response, ConsoleCookie).Should().NotBeNull(path);
        CookieHeader(response, "auth_idp").Should().NotBeNull(path);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("real-refresh-token", path);
    }

    [Theory]
    [InlineData("Auth.TokenRevoked")]
    [InlineData("Auth.RefreshTokenRevoked")]
    [InlineData("Auth.RefreshTokenExpired")]
    [InlineData("User.AccountLocked")]
    public async Task FinalRefusal_OfACookieToken_ExpiresTheCookie(string code)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var error = code switch
        {
            "Auth.TokenRevoked" => AuthErrors.TokenRevoked,
            "Auth.RefreshTokenRevoked" => AuthErrors.RefreshTokenRevoked,
            "Auth.RefreshTokenExpired" => AuthErrors.RefreshTokenExpired,
            _ => UserErrors.AccountLocked,
        };
        ArrangeRefresh(host, error);

        var response = await host.PostAsync(
            "/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=dead-token"]);

        (await ProblemCodeAsync(response)).Should().Be(code);
        CookieHeader(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=0");
    }

    [Fact]
    public async Task NonFinalRefusal_KeepsTheCookie()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        ArrangeRefresh(host, ApplicationErrors.ApplicationInactive);

        var response = await host.PostAsync(
            "/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=live-token"]);

        (await ProblemCodeAsync(response)).Should().Be(ApplicationErrors.ApplicationInactive.Code);
        CookieHeader(response, ConsoleCookie).Should().BeNull();
    }

    [Fact]
    public async Task Logout_ExpiresTheRequestingAppsCookie()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: true);
        host.Sender.Setup(s => s.Send(
                It.IsAny<Auth.Application.Features.Authentication.Logout.LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);

        var response = await host.PostAsync(
            "/api/v1/auth/logout", """{"logoutAllDevices":false}""", origin: ConsoleApp, signedIn: true);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        CookieHeader(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=0");
        CookieHeader(response, RefreshCookieOf(AccountsApp)).Should().BeNull("only the requesting app is signed out");
    }
}
