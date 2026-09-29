using System.Net;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.Login;
using Auth.Domain.Errors;
using ErrorOr;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// The same-site CSRF barrier on the real endpoints. SameSite cannot tell the
/// console from the apex or a sibling subdomain; the exact Origin match can.
/// </summary>
public class RequireFirstPartyOriginFilterTests
{
    private const string LoginBody = """{"email":"a@b.c","password":"x"}""";

    private static void ArrangeLogin(FirstPartyHost host) =>
        host.Sender.Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<LoginResponse>)SignIn());

    private static void ArrangeEndSession(FirstPartyHost host) =>
        host.Sender.Setup(s => s.Send(It.IsAny<ConfirmEndSessionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);

    private static async Task ShouldBeRefused(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
    }

    // ---- POST /auth/login: unconditional, no flag (owner decision 2026-09-28) ----

    [Theory]
    [InlineData(AccountsApp)]
    [InlineData(ConsoleApp)]
    [InlineData(null)] // SDK, Postman: no browser, no Origin
    public async Task Login_FromAListedAppOrWithoutOrigin_Passes(string? origin)
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        ArrangeLogin(host);

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(Apex)]    // a CORS origin that is not first-party
    [InlineData(Sibling)] // a same-site sibling
    [InlineData("null")]  // an opaque origin
    [InlineData("http://console.example.com")]
    public async Task Login_FromAnyOtherBrowserOrigin_IsRefused_BeforeTheHandlerRuns(string origin)
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        ArrangeLogin(host);

        var response = await host.PostAsync("/api/v1/auth/login", LoginBody, origin: origin);

        await ShouldBeRefused(response);
        host.Sender.Verify(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Login_IsRefused_EvenWithAnInvalidBody_SoTheOriginIsCheckedFirst()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);

        await ShouldBeRefused(await host.PostAsync("/api/v1/auth/login", "{ not json", origin: Apex));
    }

    [Theory]
    [InlineData(Apex)]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Login_WithAnEmptyList_PassesEveryone_AsBeforeTheList(string? origin)
    {
        await using var host = await StartAsync([], cookieEnabled: false);
        ArrangeLogin(host);

        (await host.PostAsync("/api/v1/auth/login", LoginBody, origin: origin))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- POST /auth/end-session: only while the refresh cookie delivery is on ----

    [Fact]
    public async Task EndSession_WithDeliveryOn_FromAccounts_Passes()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: true);
        ArrangeEndSession(host);

        (await host.PostAsync("/api/v1/auth/end-session", origin: AccountsApp))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(Apex)]
    [InlineData(Sibling)]
    [InlineData(null)] // a browser always sends Origin on POST; here its absence is refused
    public async Task EndSession_WithDeliveryOn_FromAnythingElse_IsRefused(string? origin)
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: true);
        ArrangeEndSession(host);

        await ShouldBeRefused(await host.PostAsync("/api/v1/auth/end-session", origin: origin));
    }

    [Theory]
    [InlineData(Apex)]
    [InlineData(null)]
    public async Task EndSession_WithDeliveryOff_BehavesAsToday(string? origin)
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        ArrangeEndSession(host);

        (await host.PostAsync("/api/v1/auth/end-session", origin: origin))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
