using Auth.Infrastructure.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for TwoFactorSecretProtector — encrypt-at-rest for TOTP secrets
/// with graceful handling of legacy plaintext rows.
/// </summary>
public class TwoFactorSecretProtectorTests
{
    private const string Secret = "JBSWY3DPEHPK3PXP"; // a Base32 TOTP secret

    private static TwoFactorSecretProtector CreateProtector() =>
        new(new EphemeralDataProtectionProvider(),
            new Mock<ILogger<TwoFactorSecretProtector>>().Object);

    [Fact]
    public void Protect_ProducesCiphertextDifferentFromPlaintext()
    {
        var protector = CreateProtector();

        var protectedValue = protector.Protect(Secret);

        protectedValue.Should().NotBe(Secret);
        protectedValue.Should().NotContain(Secret);
    }

    [Fact]
    public void ProtectThenUnprotect_RoundTripsToOriginalSecret()
    {
        var protector = CreateProtector();

        var roundTripped = protector.Unprotect(protector.Protect(Secret));

        roundTripped.Should().Be(Secret);
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_ReturnedAsIs()
    {
        // A value written before encryption existed cannot be decrypted; it must
        // be returned unchanged so the existing 2FA user is not locked out.
        var protector = CreateProtector();

        protector.Unprotect(Secret).Should().Be(Secret);
    }
}
