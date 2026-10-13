using Auth_API.Tests.Authentication.OidcUserInfo;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-142: the format of the one generator behind every opaque bearer value this server
/// issues (authorization codes, refresh tokens, the IdP session cookie, two-factor
/// challenge tokens, external-login nonces). These values cross filters the server does
/// not own: on 2026-10-12 a client's web application firewall refused the authorization
/// redirect with a 403, because standard base64 always ended in "==". The handler tests
/// replace the generator with a constant, so the real format is asserted only here.
/// </summary>
public sealed class OpaqueTokenFormatTests : IDisposable
{
    private const int Samples = 1000;

    private readonly UserInfoTokens _tokens = new();

    public void Dispose() => _tokens.Dispose();

    private string[] Sample() =>
        Enumerable.Range(0, Samples).Select(_ => _tokens.Service.GenerateRefreshToken()).ToArray();

    [Fact]
    public void GenerateRefreshToken_Is64LowercaseHexCharacters()
    {
        // 32 random bytes: 256 bits, and no punctuation for a firewall rule to match
        // ("==", "+", "/", or the "--" that base64url's "-" forms by chance).
        Sample().Should().AllSatisfy(value => value.Should().MatchRegex("^[0-9a-f]{64}$"));
    }

    [Fact]
    public void GenerateRefreshToken_SurvivesUrlEncodingUnchanged()
    {
        // What travels in a redirect or a form body is the value itself, so a filter that
        // decodes before matching sees exactly what was issued.
        Sample().Should().AllSatisfy(value => Uri.EscapeDataString(value).Should().Be(value));
    }

    [Fact]
    public void GenerateRefreshToken_NeverRepeats()
    {
        Sample().Should().OnlyHaveUniqueItems();
    }
}
