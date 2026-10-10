using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using OtpNet;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for TotpService, focused on the otpauth URI every authenticator
/// app has to parse, and on the time step a valid code reports.
/// </summary>
public class TotpServiceTests
{
    // RFC 6238's own test key, so the codes below are the same on every run.
    private const string FixedSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly TotpService _service;

    public TotpServiceTests()
    {
        _service = new TotpService(_passwordHasherMock.Object, TimeProvider.System);
    }

    [Fact]
    public void GenerateQrCodeUri_DeclaresTheParametersEveryAppNeeds()
    {
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "YourBrand");

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
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "YourBrand Console");

        uri.Should().NotContain("+");
        uri.Should().Contain("YourBrand%20Console");
    }

    [Fact]
    public void GenerateQrCodeUri_LeavesExactlyOneSeparatorInTheLabel()
    {
        // The label is "issuer:account". An unencoded ":" or "/" from either
        // half would split it in the wrong place.
        var uri = _service.GenerateQrCodeUri("JBSWY3DPEHPK3PXP", "user@example.com", "YourBrand");

        var label = uri["otpauth://totp/".Length..uri.IndexOf('?', StringComparison.Ordinal)];
        label.Should().Be("YourBrand:user%40example.com");
        label.Count(c => c == ':').Should().Be(1);
        label.Should().NotContain("/");
    }

    [Fact]
    public void ValidateCode_RejectsAnythingThatIsNotSixDigits()
    {
        var secret = _service.GenerateSecret();

        _service.ValidateCode(secret, "").Should().BeNull();
        _service.ValidateCode(secret, "12345").Should().BeNull();
        _service.ValidateCode(secret, "1234567").Should().BeNull();
    }

    [Fact]
    public void ValidateCode_ReturnsTheMatchedAbsoluteStep_ForEachWindowFrame()
    {
        // A fixed instant mid-step, read through the injected clock: the step a
        // code matched is what the commit stores, so it must come from the same
        // clock every time and be ABSOLUTE (Unix seconds / 30), never an offset
        // from now — an offset would make every code of the window look new.
        var now = new DateTimeOffset(2026, 9, 19, 9, 14, 7, TimeSpan.Zero);
        var current = now.ToUnixTimeSeconds() / 30;
        var service = new TotpService(_passwordHasherMock.Object, new FixedTimeProvider(now));
        var generator = new Totp(Base32Encoding.ToBytes(FixedSecret), step: 30, totpSize: 6);
        string CodeOf(long step) => generator.ComputeTotp(DateTimeOffset.FromUnixTimeSeconds(step * 30).UtcDateTime);

        // Precondition: five distinct codes, so no frame can match another's.
        Enumerable.Range(-2, 5).Select(offset => CodeOf(current + offset)).Should().OnlyHaveUniqueItems();

        foreach (var offset in new[] { -1, 0, 1 })
        {
            service.ValidateCode(FixedSecret, CodeOf(current + offset))
                .Should().Be(current + offset, $"the code of step now{offset:+#;-#;+0} matched that step");
        }

        // One step beyond the window either side: refused.
        service.ValidateCode(FixedSecret, CodeOf(current + 2)).Should().BeNull();
        service.ValidateCode(FixedSecret, CodeOf(current - 2)).Should().BeNull();
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
