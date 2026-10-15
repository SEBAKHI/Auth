using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-151: RP-Initiated Logout's <c>post_logout_redirect_uri</c>. The browser goes
/// back to the relying party only to an address registered, exactly, for the
/// application in <c>client_id</c>, and only once no session is live. With a live
/// session it goes to the confirmation, carrying the address as its hash, because
/// no accounts URL may hold a whole address. Every request the return cannot
/// honour behaves exactly as one that asked for no return.
/// </summary>
public class EndSessionPostLogoutRedirectTests
{
    private const string ClientId = "SHOP";
    private const string OtherClientId = "CRM";
    private const string AccountsBase = "https://accounts.example.com";
    private const string Registered = "https://shop.example.com/signed-out";
    private const string RegisteredWithQuery = "https://shop.example.com/after?from=iam";
    private const string OtherAppsReturn = "https://crm.example.com/signed-out";

    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IRefreshTokenKeyService> _tokenKeys = new();
    private readonly EndSessionCommandHandler _handler;

    public EndSessionPostLogoutRedirectTests()
    {
        _tokenKeys.Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string t) => $"hash:{t}");

        var settings = new IdentityProviderSettings { AccountsBaseUrl = AccountsBase };
        _handler = new EndSessionCommandHandler(
            _applications.Object,
            TestIdpSessions.Reader(_idpSessions, _tokenKeys.Object, settings),
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<EndSessionCommandHandler>>().Object);

        var shop = TestHelpers.CreateApplication(code: ClientId);
        shop.LoadPostLogoutRedirectUris([Registered, RegisteredWithQuery]);
        _applications.Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(shop);

        var crm = TestHelpers.CreateApplication(code: OtherClientId);
        crm.LoadPostLogoutRedirectUris([OtherAppsReturn]);
        _applications.Setup(r => r.GetByCodeAsync(OtherClientId, It.IsAny<CancellationToken>())).ReturnsAsync(crm);
    }

    /// <summary>Computed here, not with the production helper, so the test pins the format.</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void LiveSession(string token = "sso-token")
    {
        var session = new IdpSession(
            Guid.NewGuid(), Guid.NewGuid(), $"hash:{token}",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(7), null, null, null,
            lastActivityAt: DateTime.UtcNow);

        _idpSessions
            .Setup(r => r.GetByTokenHashAsync($"hash:{token}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private void NoSession() =>
        _idpSessions
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession?)null);

    private async Task<Uri> Destination(EndSessionCommand command)
    {
        var result = await _handler.Handle(command, CancellationToken.None);
        result.IsError.Should().BeFalse();
        return new Uri(result.Value.RedirectUrl);
    }

    private static Dictionary<string, string> QueryOf(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : ""));

    // --- T1: a live session goes to the confirmation, the address as its hash ---

    [Fact]
    public async Task Handle_WithLiveSessionAndRegisteredReturn_ConfirmsCarryingTheHashNotTheAddress()
    {
        LiveSession();

        var destination = await Destination(new EndSessionCommand(
            ClientId, "xyz", "sso-token", PostLogoutRedirectUri: Registered));

        destination.GetLeftPart(UriPartial.Path).Should().Be($"{AccountsBase}/logout");
        var query = QueryOf(destination);
        query.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["state"] = "xyz",
            [PostLogoutReturnHash.Parameter] = Sha256Hex(Registered),
        });
        query[PostLogoutReturnHash.Parameter].Should().MatchRegex("^[0-9a-f]{64}$");
        query.Values.Should().NotContain(value => value.Contains("://"),
            "a web application firewall may refuse an accounts page whose query holds a whole address");
        destination.Query.Should().NotContain("shop.example.com");
        _idpSessions.Verify(
            r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- T2: no session goes straight back, with state ---

    [Theory]
    [InlineData(Registered, Registered + "?state=xyz")]
    [InlineData(RegisteredWithQuery, RegisteredWithQuery + "&state=xyz")]
    public async Task Handle_WithNoSessionAndRegisteredReturn_GoesBackToItWithState(string registered, string expected)
    {
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token", PostLogoutRedirectUri: registered),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_WithNoSessionAndNoState_GoesBackToTheAddressAsRegistered()
    {
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, null, null, PostLogoutRedirectUri: Registered),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(Registered);
    }

    [Fact]
    public async Task Handle_WithNoSession_EscapesStateOnTheReturn()
    {
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "a b&c=d", null, PostLogoutRedirectUri: Registered),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Registered}?state=a%20b%26c%3Dd");
    }

    // --- T3: everything the return cannot honour behaves as if none was asked ---

    public static TheoryData<string?, string?, string?, string?> Unhonoured => new()
    {
        // client_id, state, post_logout_redirect_uri, hash
        { ClientId, "xyz", "https://SHOP.example.com/signed-out", null },                  // case
        { ClientId, "xyz", "https://shop.example.com/Signed-out", null },                  // case in the path
        { ClientId, "xyz", Registered + "/", null },                                       // trailing slash
        { ClientId, "xyz", Registered + "?extra=1", null },                                // extra query
        { ClientId, "xyz", "https://attacker.example.net/signed-out", null },              // never registered
        { ClientId, "xyz", OtherAppsReturn, null },                                        // another application's
        { ClientId, "xyz", null, Sha256Hex(OtherAppsReturn) },                             // another application's, by hash
        { ClientId, "xyz", null, Sha256Hex(Registered).ToUpperInvariant() },               // hash not as issued
        { ClientId, "xyz", null, new string('0', 64) },                                    // hash naming nothing
        { ClientId, "xyz", null, Registered },                                             // the address in the hash's place
        { null, "xyz", Registered, null },                                                 // no client_id
        { "  ", "xyz", Registered, null },                                                 // blank client_id
        { null, "xyz", null, Sha256Hex(Registered) },                                      // no client_id, by hash
        { ClientId, new string('s', 513), Registered, null },                              // state over 512
        { ClientId, new string('s', 513), null, Sha256Hex(Registered) },                   // state over 512, by hash
        { ClientId, "xyz", Registered, Sha256Hex(Registered) },                            // both at once
    };

    [Fact]
    public void Unhonoured_HasCases() => Unhonoured.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(Unhonoured))]
    public async Task Handle_WithNoSessionAndAnUnhonouredReturn_IsTheSignedOutPageAsWithoutOne(
        string? clientId, string? state, string? address, string? hash)
    {
        NoSession();

        var asked = await _handler.Handle(
            new EndSessionCommand(clientId, state, "sso-token", address, hash), CancellationToken.None);
        var without = await _handler.Handle(
            new EndSessionCommand(clientId, state, "sso-token"), CancellationToken.None);

        asked.IsError.Should().BeFalse();
        asked.Value.RedirectUrl.Should().Be(without.Value.RedirectUrl);
        asked.Value.RedirectUrl.Should().StartWith($"{AccountsBase}/signed-out");
    }

    [Theory]
    [MemberData(nameof(Unhonoured))]
    public async Task Handle_WithLiveSessionAndAnUnhonouredReturn_IsTheConfirmationWithoutAHash(
        string? clientId, string? state, string? address, string? hash)
    {
        LiveSession();

        var asked = await _handler.Handle(
            new EndSessionCommand(clientId, state, "sso-token", address, hash), CancellationToken.None);
        var without = await _handler.Handle(
            new EndSessionCommand(clientId, state, "sso-token"), CancellationToken.None);

        asked.IsError.Should().BeFalse();
        asked.Value.RedirectUrl.Should().Be(without.Value.RedirectUrl);
        asked.Value.RedirectUrl.Should().StartWith($"{AccountsBase}/logout");
        asked.Value.RedirectUrl.Should().NotContain($"{PostLogoutReturnHash.Parameter}=");
    }

    [Fact]
    public async Task Handle_WithStateOfExactly512_StillHonoursTheReturn()
    {
        NoSession();
        var state = new string('s', 512);

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, state, null, PostLogoutRedirectUri: Registered), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{Registered}?state={state}");
    }

    // --- T4: the hash, from the accounts page ---

    [Theory]
    [InlineData(Registered, Registered + "?state=xyz")]
    [InlineData(RegisteredWithQuery, RegisteredWithQuery + "&state=xyz")]
    public async Task Handle_WithTheHashAndNoSession_GoesBackToTheRegisteredAddress(string registered, string expected)
    {
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token", PostLogoutReturnHash: Sha256Hex(registered)),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_WithTheHashWhileTheSessionStillLives_AsksOnceMoreWithoutTheHash()
    {
        // The page navigates here only after its confirmation answered 204. If
        // the session still lives, that confirmation did not end it: sending the
        // browser back would tell the relying party a lie, and carrying the hash
        // again could go round for ever. The question is asked once more, plain.
        LiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token", PostLogoutReturnHash: Sha256Hex(Registered)),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/logout?client_id={ClientId}&state=xyz");
        _idpSessions.Verify(
            r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithTheAddressAndALiveSession_StillCarriesTheHash()
    {
        // Only the hash path loses it: the relying party's own request is the
        // first confirmation, and the page needs the hash to come back.
        LiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token", PostLogoutRedirectUri: Registered),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().Be(
            $"{AccountsBase}/logout?client_id={ClientId}&state=xyz&plr={Sha256Hex(Registered)}");
    }

    // --- F1: the hash for an application that went away in between ---

    public static TheoryData<string> GoneClients => new() { "GHOST", "IDLE" };

    [Fact]
    public void GoneClients_HasCases() => GoneClients.Count.Should().BeGreaterThan(0);

    private string ArrangeGoneClient(string clientId)
    {
        const string idleReturn = "https://idle.example.com/signed-out";
        if (clientId == "IDLE")
        {
            var inactive = TestHelpers.CreateApplication(code: "IDLE", isActive: false);
            inactive.LoadPostLogoutRedirectUris([idleReturn]);
            _applications.Setup(r => r.GetByCodeAsync("IDLE", It.IsAny<CancellationToken>())).ReturnsAsync(inactive);
        }
        else
        {
            _applications
                .Setup(r => r.GetByCodeAsync(clientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Auth.Domain.Entities.Application?)null);
        }

        return Sha256Hex(idleReturn);
    }

    [Theory]
    [MemberData(nameof(GoneClients))]
    public async Task Handle_WithTheHashForAGoneClientAndNoSession_IsTheSignedOutPageWithoutTheClient(string clientId)
    {
        // The person has just confirmed; the application was switched off or
        // deleted meanwhile. A 400 here would strand them on an API error page.
        var hash = ArrangeGoneClient(clientId);
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand(clientId, "xyz", null, PostLogoutReturnHash: hash), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/signed-out?state=xyz");
    }

    [Theory]
    [MemberData(nameof(GoneClients))]
    public async Task Handle_WithTheHashForAGoneClientAndALiveSession_IsTheConfirmationWithoutTheClient(string clientId)
    {
        var hash = ArrangeGoneClient(clientId);
        LiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(clientId, "xyz", "sso-token", PostLogoutReturnHash: hash), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/logout?state=xyz");
    }

    // --- T5: an unknown or inactive client keeps today's answer ---

    [Theory]
    [InlineData(Registered, null)]
    [InlineData(Registered, "0000000000000000000000000000000000000000000000000000000000000000")] // both at once
    public async Task Handle_WithAnUnknownClientAndAReturn_IsRefusedAsToday(string? address, string? hash)
    {
        _applications
            .Setup(r => r.GetByCodeAsync("GHOST", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.Application?)null);

        var result = await _handler.Handle(
            new EndSessionCommand("GHOST", "xyz", null, address, hash), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
    }

    [Fact]
    public async Task Handle_WithAnInactiveClientAndItsOwnRegisteredReturn_IsRefusedAsToday()
    {
        var inactive = TestHelpers.CreateApplication(code: "IDLE", isActive: false);
        inactive.LoadPostLogoutRedirectUris(["https://idle.example.com/signed-out"]);
        _applications.Setup(r => r.GetByCodeAsync("IDLE", It.IsAny<CancellationToken>())).ReturnsAsync(inactive);
        NoSession();

        var result = await _handler.Handle(
            new EndSessionCommand("IDLE", "xyz", null, "https://idle.example.com/signed-out"),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
    }

    [Fact]
    public void TheHashParameter_IsTheProductionHelpersFormat()
    {
        PostLogoutReturnHash.Of(Registered).Should().Be(Sha256Hex(Registered));
        PostLogoutReturnHash.Parameter.Should().Be("plr");
    }
}
