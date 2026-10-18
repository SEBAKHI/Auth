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

    // The server's request carries a device header of its own, so a rule that mixed it in fails.
    private static readonly SignInOrigin TokenRequest = new("203.0.113.7", "crm-server/2.1", "server-device-7");

    private static AuthorizationCode Code(SignInOrigin recorded) => AuthorizationCode.Create(
        Guid.NewGuid(), Guid.NewGuid(), "h", "https://crm.example.com/bff/callback", "c",
        TimeSpan.FromSeconds(60), recorded.IpAddress, ScopeSet.FromStored("openid"),
        userAgent: recorded.UserAgent, deviceId: recorded.DeviceId);

    /// <summary>A row the previous build wrote, rebuilt the way the repository rebuilds it.</summary>
    private static AuthorizationCode PreColumnCode() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "h", "https://crm.example.com/bff/callback", "c",
        DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), consumedAt: null, "198.51.100.23",
        requiresClientAuthentication: true);

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
        PreColumnCode().OriginFor(TokenRequest, clientAuthenticated: true).Should().Be(TokenRequest);
    }

    [Fact]
    public void ACodeMintedWithoutAUserAgent_StoresAnEmptyOne_AndIsRecordedAsTheBrowser()
    {
        // The domain owns the rule: whichever site mints a code, NULL stays reserved for rows
        // written before the column, so dropping the header never has the server recorded.
        var code = AuthorizationCode.Create(
            Guid.NewGuid(), Guid.NewGuid(), "h", "https://crm.example.com/bff/callback", "c",
            TimeSpan.FromSeconds(60), Browser.IpAddress, ScopeSet.FromStored("openid"),
            userAgent: null, deviceId: Browser.DeviceId);

        code.UserAgent.Should().BeEmpty();
        code.OriginFor(TokenRequest, clientAuthenticated: true)
            .Should().Be(new SignInOrigin(Browser.IpAddress, null, Browser.DeviceId));
    }

    [Fact]
    public void ABrowserThatSentNoUserAgent_IsStillRecordedAsTheBrowser()
    {
        // Authorize writes an empty user agent, never NULL: NULL is reserved for codes written
        // before the column.
        var withoutAgent = new SignInOrigin(Browser.IpAddress, string.Empty, Browser.DeviceId);

        Code(withoutAgent).OriginFor(TokenRequest, clientAuthenticated: true)
            .Should().Be(new SignInOrigin(Browser.IpAddress, null, Browser.DeviceId));
    }

    [Fact]
    public void ASignInThatSentNoDeviceId_KeepsTheBrowsersAddressAndAgent()
    {
        var withoutDevice = new SignInOrigin(Browser.IpAddress, Browser.UserAgent, null);

        Code(withoutDevice).OriginFor(TokenRequest, clientAuthenticated: true).Should().Be(withoutDevice,
            "the server's own device header, if any, is never mixed in");
    }
}
