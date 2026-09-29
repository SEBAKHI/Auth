using System.Net;
using Auth.Application.Features.Authentication.LogoutWithRefreshCookie;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// <c>POST /auth/logout/cookie</c> on the real controller: the sign-out that still
/// works when the access token is expired or refused. Without it the bearer
/// sign-out answers 401 before anything runs, and the HttpOnly refresh cookie
/// outlives a sign-out the screen reports as done (PR #17 review, F2).
/// </summary>
public class CookieSignOutTests
{
    private static readonly string ConsoleCookie = RefreshCookieOf(ConsoleApp);

    private static Func<LogoutWithRefreshCookieCommand?> Arrange(FirstPartyHost host, bool ended)
    {
        LogoutWithRefreshCookieCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<LogoutWithRefreshCookieCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<LogoutWithRefreshCookieResult>>, CancellationToken>(
                (command, _) => captured = (LogoutWithRefreshCookieCommand)command)
            .ReturnsAsync((ErrorOr<LogoutWithRefreshCookieResult>)new LogoutWithRefreshCookieResult(ended));
        return () => captured;
    }

    private static string? CookieHeader(HttpResponseMessage response, string name) =>
        SetCookies(response).SingleOrDefault(header => header.StartsWith(name + "=", StringComparison.Ordinal));

    [Fact]
    public async Task WithoutABearer_TheCookiesSessionIsEnded_AndBothCookiesAreDeleted()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var session = Guid.NewGuid();
        var command = Arrange(host, ended: true);

        // No bearer at all: the case the [Authorize] sign-out refuses with 401.
        var response = await host.PostAsync(
            "/api/v1/auth/logout/cookie", $$"""{"sessionId":"{{session}}"}""",
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=live-token", "auth_idp=sso-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("ended").GetBoolean().Should().BeTrue();
        command()!.Should().Be(new LogoutWithRefreshCookieCommand("live-token", session, "sso-token"));
        CookieHeader(response, ConsoleCookie)!.ToLowerInvariant().Should().Contain("max-age=0");
        CookieHeader(response, "auth_idp").Should().NotBeNull();
    }

    [Fact]
    public async Task ACookieThatNowBelongsToAnotherSession_IsKept()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        Arrange(host, ended: false);

        var response = await host.PostAsync(
            "/api/v1/auth/logout/cookie", $$"""{"sessionId":"{{Guid.NewGuid()}}"}""",
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=new-sign-in-token"]);

        (await JsonAsync(response)).GetProperty("ended").GetBoolean().Should().BeFalse();
        SetCookies(response).Should().BeEmpty();
    }

    [Fact]
    public async Task NoCookie_NothingToEnd()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        Arrange(host, ended: true);

        var response = await host.PostAsync("/api/v1/auth/logout/cookie", "{}", origin: ConsoleApp);

        (await JsonAsync(response)).GetProperty("ended").GetBoolean().Should().BeTrue();
        host.Sender.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Apex)]    // same site: the browser attaches the Strict cookie
    [InlineData("null")]
    [InlineData(null)]    // no Origin: no app named, so no cookie to read
    public async Task AnOriginThatNamesNoApp_CannotUseIt(string? origin)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        Arrange(host, ended: true);

        var response = await host.PostAsync(
            "/api/v1/auth/logout/cookie", "{}", origin: origin, cookies: [$"{ConsoleCookie}=live-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.Sender.Invocations.Should().BeEmpty();
    }
}
