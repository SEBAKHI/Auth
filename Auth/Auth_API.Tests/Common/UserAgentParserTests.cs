using Auth.Application.Common;

namespace Auth_API.Tests.Common;

/// <summary>
/// Mirrors the cases in Auth_UI/packages/ui/src/user-agent.ts. The two parsers
/// label the same sign-in in two places — the profile's session list and the
/// new-device email — so a disagreement reads to the user as a second,
/// unexplained sign-in.
/// </summary>
public class UserAgentParserTests
{
    [Theory]
    // Order matters: these agents all embed another brand's token.
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 Edg/120.0",
        "Microsoft Edge", "Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0 Safari/537.36 OPR/106.0",
        "Opera", "Windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 SamsungBrowser/23.0 Chrome/115.0 Mobile Safari/537.36",
        "Samsung Internet", "Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36",
        "Chrome", "Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Safari", "macOS")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Firefox", "Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0 Mobile/15E148 Safari/604.1",
        "Chrome", "iOS")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 Chrome/120.0 Safari/537.36",
        "Chrome", "ChromeOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36",
        "Chrome", "Linux")]
    public void Parse_IdentifiesBrowserAndOperatingSystem(string userAgent, string browser, string os)
    {
        var parsed = UserAgentParser.Parse(userAgent);

        parsed.Browser.Should().Be(browser);
        parsed.Os.Should().Be(os);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curl/8.4.0")]
    public void Parse_ReturnsNothingRatherThanGuessingForAnUnknownAgent(string? userAgent)
    {
        var parsed = UserAgentParser.Parse(userAgent);

        parsed.Browser.Should().BeNull();
        parsed.Os.Should().BeNull();
        parsed.Describe().Should().BeNull();
    }

    [Fact]
    public void Describe_ReadsAsALabelForTheEmailBody()
    {
        UserAgentParser
            .Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36")
            .Describe()
            .Should().Be("Chrome on Windows");
    }

    [Fact]
    public void Describe_UsesWhicheverHalfIsKnown()
    {
        // Naming half a device beats naming none of it, and beats inventing one.
        new UserAgentParser.ParsedUserAgent("Chrome", null).Describe().Should().Be("Chrome");
        new UserAgentParser.ParsedUserAgent(null, "Windows").Describe().Should().Be("Windows");
    }
}
