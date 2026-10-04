using System.Net;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The scope at the HTTP edge (OI-58), through the real <c>AuthController</c> in a
/// test host: the authorize action hands the <c>scope</c> query parameter to the
/// handler, and both grants of <c>/auth/token</c> answer with a snake_case
/// <c>scope</c> member (RFC 6749 §5.1). The handlers are stubbed; their rules
/// have their own tests.
/// </summary>
public class OAuthScopeEndpointTests
{
    [Fact]
    public async Task Authorize_PassesTheScopeQueryParameterToTheHandler()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        AuthorizeCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<AuthorizeCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<AuthorizeResult>> command, CancellationToken _) => sent = (AuthorizeCommand)command)
            .ReturnsAsync((ErrorOr<AuthorizeResult>)new AuthorizeResult { RedirectUrl = "https://app.example.com/cb?code=c" });

        var response = await host.Client.GetAsync(
            "/api/v1/auth/authorize?response_type=code&client_id=EDIS&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb" +
            "&code_challenge=" + new string('a', 43) + "&code_challenge_method=S256&state=s" +
            "&scope=openid+profile%20phone");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        sent.Should().NotBeNull();
        sent!.Scope.Should().Be("openid profile phone", "form encoding: '+' and %20 are both a space");
    }

    [Fact]
    public async Task TokenEndpoint_RefreshGrant_ReturnsTheGrantedScope()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Auth.Application.DTOs.TokenResponse>)(Tokens("rotated") with { Scope = "openid profile" }));

        var response = await host.Client.PostAsync("/api/v1/auth/token", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "r" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("scope").GetString().Should().Be("openid profile");
        body.GetProperty("refresh_token").GetString().Should().Be("rotated");
    }

    [Fact]
    public async Task TokenEndpoint_CodeGrant_ReturnsTheGrantedScope()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender
            .Setup(s => s.Send(It.IsAny<ExchangeAuthorizationCodeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<OAuthTokenResponse>)new OAuthTokenResponse
            {
                AccessToken = "a",
                ExpiresIn = 900,
                RefreshToken = "r",
                RefreshExpiresIn = 604800,
                Scope = "openid profile email phone"
            });

        var response = await host.Client.PostAsync("/api/v1/auth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "c",
                ["redirect_uri"] = "https://app.example.com/cb",
                ["client_id"] = "EDIS",
                ["code_verifier"] = new string('v', 43),
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"scope\":\"openid profile email phone\"").And.Contain("\"access_token\":\"a\"");
    }
}
