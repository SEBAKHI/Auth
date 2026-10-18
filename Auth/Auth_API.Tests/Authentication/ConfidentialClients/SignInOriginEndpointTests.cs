using System.Net;
using Auth.Application.Features.Authentication.Authorize;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// OI-178 at the HTTP edge, through the real <c>AuthController</c> in a test host: the authorize
/// navigation and its resume hand the browser's <c>User-Agent</c> header and address to the
/// handler, which records them on the code. The handlers are stubbed;
/// <see cref="ServerSideSignInOriginTests"/> follows the values through the real ones.
/// </summary>
public class SignInOriginEndpointTests
{
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";
    private const string BrowserAddress = "198.51.100.23";

    private static HttpRequestMessage Navigation(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("User-Agent", ChromeOnWindows);
        // What the gateway sets (Set, never Append), and what ClientIpResolver reads.
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", BrowserAddress);
        return request;
    }

    [Fact]
    public async Task Authorize_HandsTheBrowsersUserAgentAndAddressToTheHandler()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        AuthorizeCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<AuthorizeCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<AuthorizeResult>> command, CancellationToken _) => sent = (AuthorizeCommand)command)
            .ReturnsAsync((ErrorOr<AuthorizeResult>)new AuthorizeResult { RedirectUrl = "https://crm.example.com/cb?code=c" });

        var response = await host.Client.SendAsync(Navigation(
            "/api/v1/auth/authorize?response_type=code&client_id=CRM&redirect_uri=https%3A%2F%2Fcrm.example.com%2Fcb" +
            "&code_challenge=" + new string('a', 43) + "&code_challenge_method=S256&state=s"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        sent!.UserAgent.Should().Be(ChromeOnWindows);
        sent.IpAddress.Should().Be(BrowserAddress);
    }

    [Fact]
    public async Task ResumeAuthorize_HandsTheBrowsersUserAgentAndAddressToTheHandler()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        ResumeAuthorizationCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<ResumeAuthorizationCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<AuthorizeResult>> command, CancellationToken _) => sent = (ResumeAuthorizationCommand)command)
            .ReturnsAsync((ErrorOr<AuthorizeResult>)new AuthorizeResult { RedirectUrl = "https://crm.example.com/cb?code=c" });

        var response = await host.Client.SendAsync(Navigation(
            "/api/v1/auth/authorize/resume?client_id=CRM&auth_request=" + new string('0', 64)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        sent!.UserAgent.Should().Be(ChromeOnWindows);
        sent.IpAddress.Should().Be(BrowserAddress);
    }
}
