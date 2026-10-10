using Auth_API.Common.FirstParty;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// Which Origin is one of the platform's own apps, and the name of its refresh
/// cookie. The header values are written exactly as a browser sends them — the
/// same literal form the harness check B4 observes on the wire: scheme, host,
/// optional port, no path, no trailing slash.
/// </summary>
public class FirstPartyOriginResolverTests
{
    private const string Console = "https://console.example.com";
    private const string Accounts = "https://accounts.example.com";

    private static readonly FirstPartyOriginResolver Resolver = new([Console, Accounts]);

    [Theory]
    [InlineData(Console)]
    [InlineData(Accounts)]
    [InlineData("https://localhost:5173")] // the dev form, with a port
    public void ABrowserOriginHeader_ForAListedApp_Resolves(string header)
    {
        var resolver = new FirstPartyOriginResolver([Console, Accounts, "https://localhost:5173"]);

        resolver.Resolve(header).Should().NotBeNull();
        resolver.Resolve(header)!.Origin.Should().Be(header);
    }

    [Theory]
    [InlineData("HTTPS://CONSOLE.EXAMPLE.COM")]
    [InlineData("https://console.example.com/")]
    [InlineData(" https://console.example.com ")]
    public void CaseAndATrailingSlash_AreNormalized(string header) =>
        Resolver.Resolve(header)!.Origin.Should().Be(Console);

    [Theory]
    [InlineData("https://example.com")]                 // the apex: same site, not an app
    [InlineData("https://evil.example.com")]            // a sibling subdomain
    [InlineData("https://console.example.com.evil.io")] // a suffix trick
    [InlineData("http://console.example.com")]          // http
    [InlineData("https://console.example.com/login")]   // an origin with a path
    [InlineData("https://console.example.com:8443")]    // another port
    [InlineData("null")]                                // an opaque origin
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNotFirstParty(string? header) =>
        Resolver.Resolve(header).Should().BeNull();

    [Fact]
    public void AnHttpEntryInTheList_NeverMakesAnHttpOriginFirstParty() =>
        new FirstPartyOriginResolver(["http://console.example.com"]).Resolve("http://console.example.com")
            .Should().BeNull("an http page can never hold a __Host- cookie");

    [Fact]
    public void AnEmptyList_KnowsNoApp()
    {
        var resolver = new FirstPartyOriginResolver([]);

        resolver.HasOrigins.Should().BeFalse();
        resolver.Resolve(Console).Should().BeNull();
    }

    [Fact]
    public void TheCookieName_IsHostPrefixed_AndDerivedFromTheOriginAlone()
    {
        var name = Resolver.Resolve(Console)!.RefreshCookieName;

        name.Should().MatchRegex("^__Host-auth_rt_[0-9a-f]{16}$");
        name.Should().Be(FirstPartyOriginResolver.CookieNameFor(Console));
        new FirstPartyOriginResolver([Accounts, Console]).Resolve(Console)!.RefreshCookieName
            .Should().Be(name, "reordering the configured list must not rename a live cookie");
    }

    [Fact]
    public void EachApp_HasItsOwnCookie() =>
        Resolver.Resolve(Console)!.RefreshCookieName
            .Should().NotBe(Resolver.Resolve(Accounts)!.RefreshCookieName,
                "one shared cookie would make the console and accounts spend each other's token");
}
