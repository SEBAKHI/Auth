using System.Net;
using System.Reflection;
using Auth.Application.DTOs;
using Auth.Application.Features.Users.GetAccountBar;
using Auth.Domain.Errors;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.UserManagement;

/// <summary>
/// The account bar endpoint on the real controller: what a browser receives for each Origin
/// and each credential. Only the accounts app's bar pages may read it, by the single sign-on
/// cookie alone.
/// </summary>
public class AccountBarEndpointTests
{
    private const string Path = "/api/v1/auth/account-bar";
    private const string Cookie = "auth_idp=session-cookie";

    private static Task<Authentication.FirstParty.FirstPartyHost> StartHostAsync(string? accountsBaseUrl = AccountsApp) =>
        StartAsync(
            [ConsoleApp, AccountsApp],
            cookieEnabled: false,
            configureIdp: settings => settings.AccountsBaseUrl = accountsBaseUrl ?? string.Empty);

    private static async Task<HttpResponseMessage> GetAsync(
        Authentication.FirstParty.FirstPartyHost host, string? origin, bool cookie = true, bool signedIn = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookie) request.Headers.TryAddWithoutValidation("Cookie", Cookie);
        if (signedIn) request.Headers.TryAddWithoutValidation("X-Test-User", Guid.NewGuid().ToString());
        return await host.Client.SendAsync(request);
    }

    private static void Answers(Authentication.FirstParty.FirstPartyHost host, AccountBarResult result, Action<GetAccountBarQuery>? seen = null) =>
        host.Sender
            .Setup(s => s.Send(It.IsAny<GetAccountBarQuery>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<AccountBarResult>> query, CancellationToken _) => seen?.Invoke((GetAccountBarQuery)query))
            .ReturnsAsync((ErrorOr<AccountBarResult>)result);

    [Fact]
    public void TheAction_IsCookieAuthenticated_AndAnswersTheAccountsOriginOnly()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.GetAccountBar))!;

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull(
            "the frame arrives with the single sign-on cookie, never a bearer token");
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequireAccountsOriginAttribute>().Should().NotBeNull(
            "a page on a sibling subdomain is same-site and would be sent the cookie");
        method.GetCustomAttribute<EnableRateLimitingAttribute>().Should().BeNull(
            "its throttle is its own gateway route on the 'api' policy (AccountBarGatewayRouteTests)");
    }

    [Theory]
    [InlineData(ConsoleApp)]
    [InlineData(Sibling)]
    [InlineData(Apex)]
    [InlineData("null")]
    [InlineData("http://accounts.example.com")]
    [InlineData(null)]
    public async Task AnyOtherOrigin_OrNone_IsRefused_BeforeTheHandler(string? origin)
    {
        await using var host = await StartHostAsync();

        var response = await GetAsync(host, origin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.Sender.Verify(s => s.Send(It.IsAny<GetAccountBarQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithNoAccountsAddressConfigured_EveryOriginIsRefused()
    {
        await using var host = await StartHostAsync(accountsBaseUrl: null);

        var response = await GetAsync(host, AccountsApp);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Sender.Verify(s => s.Send(It.IsAny<GetAccountBarQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABearerWithoutTheCookie_IdentifiesNobody()
    {
        await using var host = await StartHostAsync();
        GetAccountBarQuery? sent = null;
        Answers(host, AccountBarResult.SignIn, query => sent = query);

        var response = await GetAsync(host, AccountsApp, cookie: false, signedIn: true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be("Http.Unauthenticated");
        sent!.IdpSessionToken.Should().BeNull("only the single sign-on cookie identifies the person here");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task TheCookie_IsForwarded_AndTheAnswerIsNeverStored()
    {
        await using var host = await StartHostAsync();
        GetAccountBarQuery? sent = null;
        Answers(
            host,
            new AccountBarResult(false, new AccountBarDto
            {
                Name = "Maya Haddad",
                Email = "maya@example.com",
                Applications = [new MyApplicationDto { Id = Guid.NewGuid(), Name = "Crm", LaunchUrl = "https://crm.example.com/" }]
            }),
            query => sent = query);

        var response = await GetAsync(host, AccountsApp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sent!.IdpSessionToken.Should().Be("session-cookie");
        var json = await JsonAsync(response);
        json.GetProperty("name").GetString().Should().Be("Maya Haddad");
        json.GetProperty("applications")[0].GetProperty("launchUrl").GetString().Should().Be("https://crm.example.com/");
        response.Headers.CacheControl!.NoStore.Should().BeTrue("the answer names the person");
    }
}
