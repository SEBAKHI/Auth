using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for TotpService, focused on the otpauth URI every authenticator
/// app has to parse.
/// </summary>
public class TotpServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly TotpService _service;

    public TotpServiceTests()
    {
        _service = new TotpService(_passwordHasherMock.Object);
    }

    [Fact]
    public void GenerateQrCodeUri_DeclaresTheParametersEveryAppNeeds()
    {
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "Sebakhi");

        // RFC 6238 defaults, stated explicitly rather than left to the app to
        // assume — they are what makes any authenticator compatible.
        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("algorithm=SHA1");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
        uri.Should().Contain("secret=JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void GenerateQrCodeUri_EncodesASpacedIssuerWithoutAPlusSign()
    {
        // HttpUtility.UrlEncode is form encoding: it renders a space as "+",
        // and several otpauth parsers then show a literal plus in the account
        // name. Uri.EscapeDataString emits %20.
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "Sebakhi Console");

        uri.Should().NotContain("+");
        uri.Should().Contain("Sebakhi%20Console");
    }

    [Fact]
    public void GenerateQrCodeUri_LeavesExactlyOneSeparatorInTheLabel()
    {
        // The label is "issuer:account". An unencoded ":" or "/" from either
        // half would split it in the wrong place.
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "Sebakhi");

        var label = uri["otpauth://totp/".Length..uri.IndexOf('?', StringComparison.Ordinal)];
        label.Should().Be("Sebakhi:user%40example.com");
        label.Count(c => c == ':').Should().Be(1);
        label.Should().NotContain("/");
    }

    [Fact]
    public void ValidateCode_RejectsAnythingThatIsNotSixDigits()
    {
        var secret = _service.GenerateSecret();

        _service.ValidateCode(secret, "").Should().BeFalse();
        _service.ValidateCode(secret, "12345").Should().BeFalse();
        _service.ValidateCode(secret, "1234567").Should().BeFalse();
    }

    [Fact]
    public void GenerateSecret_ProducesADistinctBase32SecretEachTime()
    {
        var first = _service.GenerateSecret();
        var second = _service.GenerateSecret();

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[A-Z2-7]+=*$");
    }
}
