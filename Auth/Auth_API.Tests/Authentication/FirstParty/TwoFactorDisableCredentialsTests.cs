using System.Net;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// <c>POST /auth/2fa/disable</c> on the real controller: switching two-factor off signs
/// every OTHER session and browser out, so the controller must hand the command the
/// caller's own session (the token's <c>sid</c>) and SSO cookie — the plain value,
/// as change-password does. A regression that dropped either would sign the caller's
/// own browser out of single sign-on, and nothing else would turn red (OI-45 (1),
/// C-F2). Run with the refresh cookie on and off: the same request reaches the same
/// command in both modes.
/// </summary>
public class TwoFactorDisableCredentialsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disable_HandsTheCommandTheCallersSessionAndSsoCookie(bool refreshCookieEnabled)
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: refreshCookieEnabled);
        DisableTwoFactorCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<DisableTwoFactorCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<Success>>, CancellationToken>((command, _) => captured = (DisableTwoFactorCommand)command)
            .ReturnsAsync(Result.Success);
        var session = Guid.NewGuid();

        var response = await host.PostAsync(
            "/api/v1/auth/2fa/disable",
            """{"code":"123456","useRecoveryCode":false}""",
            origin: ConsoleApp,
            cookies: ["auth_idp=sso-token-of-this-browser"],
            signedIn: true,
            sessionId: session);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().NotBeNull();
        captured!.IdpSessionToken.Should().Be("sso-token-of-this-browser",
            "the caller's own SSO session is the one the revocation spares");
        captured.CurrentSessionId.Should().Be(session, "the caller's own session is spared, and measured for recency");
        captured.UserId.Should().NotBeEmpty();
        captured.Code.Should().Be("123456");
        captured.UseRecoveryCode.Should().BeFalse();
    }

    [Fact]
    public async Task Disable_WithoutAnSsoCookie_SparesNoSsoSession()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        DisableTwoFactorCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<DisableTwoFactorCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<Success>>, CancellationToken>((command, _) => captured = (DisableTwoFactorCommand)command)
            .ReturnsAsync(Result.Success);

        await host.PostAsync(
            "/api/v1/auth/2fa/disable", """{"code":"123456"}""", origin: ConsoleApp, signedIn: true, sessionId: Guid.NewGuid());

        captured!.IdpSessionToken.Should().BeNull("a browser with no SSO session has none to spare");
    }
}
