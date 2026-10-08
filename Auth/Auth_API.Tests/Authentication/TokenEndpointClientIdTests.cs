using System.Net;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using MediatR;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-101 at the HTTP edge, through the real <c>AuthController</c> in a test host:
/// the token endpoint's refresh grant hands its <c>client_id</c> to the command,
/// which is what binds an application's token to its client and earns it the
/// application replay grace. The first-party refresh endpoint never sets one. The
/// handler is stubbed; its rules have their own tests.
/// </summary>
public class TokenEndpointClientIdTests
{
    private static Func<RefreshTokenCommand?> Capture(FirstPartyHost host)
    {
        RefreshTokenCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<TokenResponse>>, CancellationToken>((command, _) => captured = (RefreshTokenCommand)command)
            .ReturnsAsync((ErrorOr<TokenResponse>)Tokens());
        return () => captured;
    }

    private static Task<HttpResponseMessage> PostRefreshGrant(FirstPartyHost host, string? clientId)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "r" };
        if (clientId is not null)
        {
            form["client_id"] = clientId;
        }

        return host.Client.PostAsync("/api/v1/auth/token", new FormUrlEncodedContent(form));
    }

    [Fact]
    public async Task TokenEndpoint_RefreshGrant_PassesTheClientIdToTheCommand()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var command = Capture(host);

        var response = await PostRefreshGrant(host, "EDIS");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        command()!.ClientId.Should().Be("EDIS");
        command()!.RefreshToken.Should().Be("r");
        command()!.ReplayGraceEligible.Should().BeFalse("only the first-party cookie earns the cookie window");
    }

    [Fact]
    public async Task TokenEndpoint_RefreshGrant_WithoutClientId_PassesNone()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var command = Capture(host);

        await PostRefreshGrant(host, clientId: null);

        command().Should().NotBeNull();
        command()!.ClientId.Should().BeNull("an integration that sends none keeps today's behaviour");
    }

    [Fact]
    public async Task TheFirstPartyRefreshEndpoint_NeverSetsAClientId()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: true);
        var command = Capture(host);

        await host.PostAsync("/api/v1/auth/refresh", """{"refreshToken":"body-token"}""", origin: ConsoleApp);

        command().Should().NotBeNull();
        command()!.ClientId.Should().BeNull();
    }
}
