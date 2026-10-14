using System.Net;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// OI-154 through the HTTP pipeline, read off the <c>Set-Cookie</c> headers a
/// browser receives: "Stay signed in" decides whether the IdP session cookie and
/// the app's refresh cookie outlive the browser, and a sign-in ends the IdP session
/// it replaces. The mediator answers from the command it actually received, so the
/// chain from the request body to the header is the real one; what the builder
/// makes of the flag is <c>LoginResponseBuilderStaySignedInTests</c>.
/// </summary>
public class StaySignedInCookieTests
{
    private const string LoginPath = "/api/v1/auth/login";
    private static readonly string ConsoleCookie = RefreshCookieOf(ConsoleApp);

    /// <summary>
    /// Makes <paramref name="host"/>'s reader find a live IdP session for the
    /// cookie value <paramref name="token"/>.
    /// </summary>
    internal static void PresentsSession(FirstPartyHost host, string token, bool persistent) =>
        host.IdpSessions
            .Setup(r => r.FindLiveAsync(token, IdpSessionReadMode.Check, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdpSession(
                Guid.NewGuid(), Guid.NewGuid(), "hash", DateTime.UtcNow, DateTime.UtcNow.AddHours(10), null, null, null,
                isPersistent: persistent));

    /// <summary>The login answers as the builder does: persistent exactly when the command asked.</summary>
    private static void AnswerLoginFromTheCommand(FirstPartyHost host) =>
        host.Sender
            .Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<LoginResponse>> request, CancellationToken _) =>
                Task.FromResult((ErrorOr<LoginResponse>)SignIn(persistent: ((LoginCommand)request).StaySignedIn)));

    private static string? Header(HttpResponseMessage response, string name) =>
        SetCookies(response).SingleOrDefault(header => header.StartsWith(name + "=", StringComparison.Ordinal));

    private static void ShouldBeABrowserSessionCookie(string? header, string because)
    {
        header.Should().NotBeNull(because);
        header!.ToLowerInvariant().Should().NotContain("max-age", because)
            .And.NotContain("expires", because);
    }

    // --- sign-in -------------------------------------------------------------

    [Fact]
    public async Task SignIn_WithStaySignedIn_BothCookiesCarryTheirLifetimes()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        AnswerLoginFromTheCommand(host);

        var response = await host.PostAsync(
            LoginPath, """{"email":"a@b.c","password":"x","staySignedIn":true}""", origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "auth_idp")!.ToLowerInvariant().Should().Contain("max-age=604800",
            "the IdP session cookie lasts IdpSessionLifetimeDays (7) when the person stays signed in");
        Header(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=604800",
            "and the app's refresh cookie lasts its token's lifetime");
    }

    [Theory]
    [InlineData("""{"email":"a@b.c","password":"x"}""")]
    [InlineData("""{"email":"a@b.c","password":"x","staySignedIn":false}""")]
    public async Task SignIn_WithoutStaySignedIn_BothCookiesEndWithTheBrowser(string body)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        AnswerLoginFromTheCommand(host);

        var response = await host.PostAsync(LoginPath, body, origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, "auth_idp"), "an unticked sign-in is a browser session");
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "and so is the app's own sign-in (D-154-1 (a))");
    }

    public static TheoryData<string, string, Type> CarryingExits => new()
    {
        { LoginPath, """{"email":"a@b.c","password":"x"STAY}""", typeof(LoginCommand) },
        { "/api/v1/auth/external-login", """{"provider":"google","idToken":"x"STAY}""", typeof(ExternalLoginCommand) },
        { "/api/v1/auth/2fa/verify", """{"challengeToken":"c","code":"123456"STAY}""", typeof(VerifyTwoFactorLoginCommand) },
    };

    [Theory]
    [MemberData(nameof(CarryingExits))]
    public async Task TheThreeCarryingExits_PassTheChoiceToTheirCommand_AndAbsentIsFalse(
        string path, string bodyTemplate, Type commandType)
    {
        foreach (var (stay, expected) in new[] { (",\"staySignedIn\":true", true), ("", false) })
        {
            await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
            object? received = null;
            host.Sender
                .Setup(s => s.Send(It.IsAny<IRequest<ErrorOr<LoginResponse>>>(), It.IsAny<CancellationToken>()))
                .Callback((IRequest<ErrorOr<LoginResponse>> request, CancellationToken _) => received = request)
                .ReturnsAsync((ErrorOr<LoginResponse>)SignIn());

            var response = await host.PostAsync(path, bodyTemplate.Replace("STAY", stay), origin: ConsoleApp);

            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            received.Should().BeOfType(commandType, path);
            commandType.GetProperty("StaySignedIn")!.GetValue(received).Should().Be(expected, $"{path} with '{stay}'");
        }
    }

    // --- refresh -------------------------------------------------------------

    private static async Task<HttpResponseMessage> RefreshAsync(FirstPartyHost host, bool presentIdpCookie = true)
    {
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<TokenResponse>)Tokens("rotated-token", refreshExpiresIn: 1234));

        string[] cookies = presentIdpCookie
            ? [$"{ConsoleCookie}=cookie-token", "auth_idp=sso-token"]
            : [$"{ConsoleCookie}=cookie-token"];
        return await host.PostAsync("/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: cookies);
    }

    [Fact]
    public async Task Refresh_PresentingALivePersistentSession_KeepsTheCookiesLifetime()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        PresentsSession(host, "sso-token", persistent: true);

        var response = await RefreshAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=1234");
        Header(response, "auth_idp").Should().BeNull("a refresh issues no IdP session");
    }

    [Fact]
    public async Task Refresh_PresentingABrowserSession_WritesABrowserSessionCookie()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        PresentsSession(host, "sso-token", persistent: false);

        var response = await RefreshAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "the presented session ends with the browser");
    }

    [Fact]
    public async Task Refresh_PresentingNoLiveSession_WritesABrowserSessionCookie()
    {
        // The reader answers null for an idle, revoked, expired or unknown session.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);

        var response = await RefreshAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "no live session stays signed in");
        host.IdpSessions.Verify(
            r => r.FindLiveAsync("sso-token", IdpSessionReadMode.Check, It.IsAny<CancellationToken>()), Times.Once,
            "the persistence read records no use");
        host.IdpSessions.Verify(
            r => r.FindLiveAsync(It.IsAny<string?>(), IdpSessionReadMode.Use, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_PresentingNoIdpCookie_WritesABrowserSessionCookie()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);

        var response = await RefreshAsync(host, presentIdpCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "no IdP session was presented");
    }

    [Fact]
    public async Task Refresh_WhenTheSessionCannotBeRead_AnswersAsABrowserSession_AndStillSucceeds()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        host.IdpSessions
            .Setup(r => r.FindLiveAsync(It.IsAny<string?>(), It.IsAny<IdpSessionReadMode>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var response = await RefreshAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBeABrowserSessionCookie(Header(response, ConsoleCookie), "a failed read answers 'not persistent'");
        host.Warnings.Should().Contain(message => message.StartsWith("IdpSession.PersistenceUnread", StringComparison.Ordinal));
    }

    // --- a new sign-in ends the session it replaces ---------------------------

    [Fact]
    public async Task SignIn_CarryingAnotherIdpCookie_EndsThatSession_AndWritesTheNewOne()
    {
        // The filter never asks whose session it was: another person's (shared
        // computer) or the same person's, ticked or not, the old one ends.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        AnswerLoginFromTheCommand(host);

        var response = await host.PostAsync(
            LoginPath, """{"email":"b@b.c","password":"x"}""", origin: ConsoleApp, cookies: ["auth_idp=previous-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Revocation.Verify(r => r.RevokeIdpSessionAsync("previous-token", It.IsAny<CancellationToken>()), Times.Once);
        Header(response, "auth_idp").Should().StartWith("auth_idp=idp-session-token;");
    }

    [Fact]
    public async Task SignIn_WhenEndingThePreviousSessionFails_StillSignsIn()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        AnswerLoginFromTheCommand(host);
        host.Revocation
            .Setup(r => r.RevokeIdpSessionAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var response = await host.PostAsync(
            LoginPath, """{"email":"b@b.c","password":"x"}""", origin: ConsoleApp, cookies: ["auth_idp=previous-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "auth_idp").Should().StartWith("auth_idp=idp-session-token;");
        host.Warnings.Should().Contain(message => message.StartsWith("IdpSession.ReplacedNotEnded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignIn_WithoutAPreviousIdpCookie_EndsNothing()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        AnswerLoginFromTheCommand(host);

        var response = await host.PostAsync(LoginPath, """{"email":"a@b.c","password":"x"}""", origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Revocation.Verify(
            r => r.RevokeIdpSessionAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_CarryingAnIdpCookie_EndsNothing()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        PresentsSession(host, "sso-token", persistent: false);

        var response = await RefreshAsync(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Revocation.Verify(
            r => r.RevokeIdpSessionAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "a refresh issues no IdP session, so it replaces none");
    }

    [Fact]
    public async Task TwoFactorChallenge_EndsNothingYet()
    {
        // The password step issues no session: the previous one ends when the
        // second factor completes the sign-in, not before.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        host.Sender
            .Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<LoginResponse>)new LoginResponse { RequiresTwoFactor = true, TwoFactorChallengeToken = "c" });

        var response = await host.PostAsync(
            LoginPath, """{"email":"a@b.c","password":"x"}""", origin: ConsoleApp, cookies: ["auth_idp=previous-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Revocation.Verify(
            r => r.RevokeIdpSessionAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
