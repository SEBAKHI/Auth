using System.Net;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// Which refresh token the real refresh action spends: a real body token first,
/// then the cookie of the app the Origin names — and never the cookie of an app the
/// Origin does not name, even when the browser attached it.
/// </summary>
public class RefreshCredentialSourceTests
{
    private static readonly string ConsoleCookie = RefreshCookieOf(ConsoleApp);

    private static Func<RefreshTokenCommand?> Capture(FirstPartyHost host)
    {
        RefreshTokenCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<TokenResponse>>, CancellationToken>((command, _) => captured = (RefreshTokenCommand)command)
            .ReturnsAsync((ErrorOr<TokenResponse>)Tokens());
        return () => captured;
    }

    private void VerifyNeverSent(FirstPartyHost host) =>
        host.Sender.Verify(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()), Times.Never());

    [Fact]
    public async Task ARealBodyToken_WinsOverTheCookie_AndIsNotGraceEligible()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", """{"refreshToken":"body-token"}""",
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        command()!.RefreshToken.Should().Be("body-token");
        command()!.ReplayGraceEligible.Should().BeFalse();
    }

    [Fact]
    public async Task TheSentinelInTheBody_IsIgnored_AndTheCookieIsSpent()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", """{"refreshToken":"__cookie__"}""",
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        command()!.RefreshToken.Should().Be("cookie-token");
        command()!.ReplayGraceEligible.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]                    // what the app sends
    [InlineData("""{"refreshToken":""}""")] // an absent member binds as an empty string
    public async Task AnEmptyBody_FromAListedApp_RefreshesByTheCookieAlone(string body)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var command = Capture(host);

        var response = await host.PostAsync("/api/v1/auth/refresh", body,
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        command()!.RefreshToken.Should().Be("cookie-token");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ACookieCredential_EarnsTheReplayGrace_OnlyWhileTheDeliveryIsOn(bool cookieEnabled, bool eligible)
    {
        // With the switch off a non-browser client can still send a listed Origin
        // and a forged cookie header carrying a stolen token. It must meet reuse
        // detection (handler test: a late non-eligible presentation revokes all),
        // not the one-time grace answer.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=stolen-token"]);

        command()!.RefreshToken.Should().Be("stolen-token");
        command()!.ReplayGraceEligible.Should().Be(eligible);
    }

    [Fact]
    public async Task TheCookieIsReadWithTheDeliverySwitchedOff_SoTurningItOffSignsNoOneOut()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", "{}", origin: ConsoleApp, cookies: [$"{ConsoleCookie}=cookie-token"]);

        command()!.RefreshToken.Should().Be("cookie-token");
    }

    [Theory]
    [InlineData(Apex)]     // same site: the browser attaches the Strict cookie
    [InlineData(Sibling)]
    [InlineData("null")]
    [InlineData(null)]
    public async Task AValidCookie_FromAnOriginThatIsNotAListedApp_IsNeverSpent(string? origin)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        Capture(host);

        var response = await host.PostAsync("/api/v1/auth/refresh", "{}",
            origin: origin, cookies: [$"{ConsoleCookie}=cookie-token"]);

        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.RefreshTokenNotFound.Code);
        VerifyNeverSent(host);
    }

    [Fact]
    public async Task AnotherAppsCookie_IsNotSpentByThisApp()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: true);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", "{}", origin: ConsoleApp,
            cookies: [$"{RefreshCookieOf(AccountsApp)}=accounts-token", $"{ConsoleCookie}=console-token"]);

        command()!.RefreshToken.Should().Be("console-token", "the Origin alone chooses the cookie (E1)");
    }

    [Fact]
    public async Task NoCredentialAtAll_IsRefreshTokenNotFound_WithoutReachingTheHandler()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        Capture(host);

        var response = await host.PostAsync("/api/v1/auth/refresh", "{}", origin: ConsoleApp);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.RefreshTokenNotFound.Code);
        VerifyNeverSent(host);
    }

    [Fact]
    public async Task AFinalRefusal_OfTheCookieToken_ExpiresTheCookie()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        host.Sender.Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<TokenResponse>)AuthErrors.RefreshTokenExpired);

        var response = await host.PostAsync("/api/v1/auth/refresh", "{}",
            origin: ConsoleApp, cookies: [$"{ConsoleCookie}=expired-token"]);

        SetCookies(response).Should().ContainSingle(header =>
            header.StartsWith(ConsoleCookie + "=", StringComparison.Ordinal) &&
            header.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }
}
