using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// The one rule of OI-178 (<see cref="AuthorizationCode.OriginFor"/>): a sign-in completed by a
/// server that authenticated is recorded where the browser was at authorize; one completed by
/// the browser itself, or from a code written before the columns, is recorded from the token
/// request, as before.
/// </summary>
public class AuthorizationCodeOriginTests
{
    private static readonly SignInOrigin Browser = new("198.51.100.23",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36",
        "0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly SignInOrigin TokenRequest = new("203.0.113.7", "crm-server/2.1", null);

    private static AuthorizationCode Code(SignInOrigin? recorded) => AuthorizationCode.Create(
        Guid.NewGuid(), Guid.NewGuid(), "h", "https://crm.example.com/bff/callback", "c",
        TimeSpan.FromSeconds(60), recorded?.IpAddress ?? "198.51.100.23", ScopeSet.FromStored("openid"),
        userAgent: recorded?.UserAgent, deviceId: recorded?.DeviceId);

    [Fact]
    public void AnAuthenticatedRedemption_IsRecordedWhereTheBrowserWas()
    {
        Code(Browser).OriginFor(TokenRequest, clientAuthenticated: true).Should().Be(Browser);
    }

    [Fact]
    public void APublicRedemption_IsRecordedFromTheTokenRequest_AsBefore()
    {
        // The token request IS the browser for a public client.
        Code(Browser).OriginFor(TokenRequest, clientAuthenticated: false).Should().Be(TokenRequest);
    }

    [Fact]
    public void ACodeWrittenBeforeTheColumns_IsRecordedFromTheTokenRequest_AsBefore()
    {
        // E3: no user agent on the row means the row predates the columns; its address alone is
        // not taken, so nothing changes for codes minted by the previous build during a deploy.
        Code(recorded: null).OriginFor(TokenRequest, clientAuthenticated: true).Should().Be(TokenRequest);
    }

    [Fact]
    public void ASignInThatSentNoDeviceId_KeepsTheBrowsersAddressAndAgent()
    {
        var withoutDevice = new SignInOrigin(Browser.IpAddress, Browser.UserAgent, null);

        Code(withoutDevice).OriginFor(TokenRequest, clientAuthenticated: true).Should().Be(withoutDevice,
            "the server's own device header, if any, is never mixed in");
    }
}
