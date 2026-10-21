using System.Net;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// OI-151 through the real <c>GET /auth/end-session</c> action: the query names a
/// relying party sends (<c>post_logout_redirect_uri</c>) and the one the accounts
/// page sends (<c>plr</c>) reach the handler, and the browser gets a 302 to where
/// the handler decided. The handler is the real one, over mocked stores.
/// </summary>
public class EndSessionReturnEndpointTests
{
    private const string Registered = "https://shop.example.com/signed-out?from=iam";
    private const string IdpCookie = "auth_idp";

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<FirstPartyHost> StartWithRealHandlerAsync(bool liveSession)
    {
        var host = await StartAsync([AccountsApp], cookieEnabled: false);

        var applications = new Mock<IApplicationRepository>();
        var shop = TestHelpers.CreateApplication(code: "SHOP");
        shop.LoadPostLogoutRedirectUris([Registered]);
        applications.Setup(r => r.GetByCodeAsync("SHOP", It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var idle = TestHelpers.CreateApplication(code: "IDLE", isActive: false);
        idle.LoadPostLogoutRedirectUris([Registered]);
        applications.Setup(r => r.GetByCodeAsync("IDLE", It.IsAny<CancellationToken>())).ReturnsAsync(idle);

        var sessions = new Mock<IIdpSessionRepository>();
        sessions
            .Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("hash:sso-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveSession
                ? new IdpSession(
                    Guid.NewGuid(), Guid.NewGuid(), "hash:sso-token",
                    DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(7), null, null, null,
                    lastActivityAt: DateTime.UtcNow)
                : null);
        var keys = new FakeCredentialHasher();
        keys.MapAll(t => $"hash:{t}");

        var settings = new IdentityProviderSettings { AccountsBaseUrl = AccountsApp };
        var handler = new EndSessionCommandHandler(
            applications.Object,
            TestIdpSessions.Reader(sessions, keys, settings),
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<EndSessionCommandHandler>>().Object);

        host.Sender
            .Setup(s => s.Send(It.IsAny<EndSessionCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<EndSessionResult>> request, CancellationToken token) =>
                handler.Handle((EndSessionCommand)request, token));

        return host;
    }

    private static Task<HttpResponseMessage> GetAsync(FirstPartyHost host, string query, bool withCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/auth/end-session?{query}");
        if (withCookie)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{IdpCookie}=sso-token");
        }

        return host.Client.SendAsync(request);
    }

    [Fact]
    public async Task EndSession_FromTheRelyingPartyWithASession_Returns302ToTheConfirmationWithTheHash()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: true);

        var response = await GetAsync(
            host,
            $"client_id=SHOP&state=xyz&post_logout_redirect_uri={Uri.EscapeDataString(Registered)}",
            withCookie: true);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(
            $"{AccountsApp}/logout?client_id=SHOP&state=xyz&plr={Sha256Hex(Registered)}");
    }

    [Fact]
    public async Task EndSession_FromTheRelyingPartyWithNoSession_Returns302ToTheRegisteredAddress()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: false);

        var response = await GetAsync(
            host,
            $"client_id=SHOP&state=xyz&post_logout_redirect_uri={Uri.EscapeDataString(Registered)}",
            withCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{Registered}&state=xyz");
    }

    [Fact]
    public async Task EndSession_FromTheAccountsPageAfterTheSessionEnded_Returns302ToTheRegisteredAddress()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: false);

        var response = await GetAsync(
            host, $"client_id=SHOP&state=xyz&plr={Sha256Hex(Registered)}", withCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{Registered}&state=xyz");
    }

    [Fact]
    public async Task EndSession_FromTheAccountsPageWhileTheSessionLives_Returns302BackToTheConfirmationWithoutTheHash()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: true);

        var response = await GetAsync(
            host, $"client_id=SHOP&state=xyz&plr={Sha256Hex(Registered)}", withCookie: true);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{AccountsApp}/logout?client_id=SHOP&state=xyz");
    }

    [Fact]
    public async Task EndSession_FromTheAccountsPageForAnApplicationSwitchedOffMeanwhile_Returns302ToTheSignedOutPage()
    {
        // Not a 400: the person has just confirmed, and an API error page would
        // leave them nowhere to go.
        await using var host = await StartWithRealHandlerAsync(liveSession: false);

        var response = await GetAsync(
            host, $"client_id=IDLE&state=xyz&plr={Sha256Hex(Registered)}", withCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{AccountsApp}/signed-out?state=xyz");
    }

    [Fact]
    public async Task EndSession_FromTheRelyingPartyForAnInactiveApplication_Returns400AsBefore()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: false);

        var response = await GetAsync(
            host,
            $"client_id=IDLE&state=xyz&post_logout_redirect_uri={Uri.EscapeDataString(Registered)}",
            withCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be(Auth.Domain.Errors.AuthErrors.InvalidClient.Code);
    }

    [Fact]
    public async Task EndSession_WithAnUnregisteredAddress_Returns302ToTheSignedOutPageAsToday()
    {
        await using var host = await StartWithRealHandlerAsync(liveSession: false);

        var response = await GetAsync(
            host,
            $"client_id=SHOP&state=xyz&post_logout_redirect_uri={Uri.EscapeDataString("https://attacker.example.net/")}",
            withCookie: false);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"{AccountsApp}/signed-out?client_id=SHOP&state=xyz");
    }
}
