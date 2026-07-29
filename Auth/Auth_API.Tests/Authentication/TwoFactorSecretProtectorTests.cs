using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for TwoFactorSecretProtector — TOTP secrets encrypted at rest
/// under the per-user DEK, with dual-read of legacy app-level (v1) payloads
/// and pre-encryption plaintext rows.
/// </summary>
public class TwoFactorSecretProtectorTests
{
    private const string Secret = "JBSWY3DPEHPK3PXP"; // a Base32 TOTP secret

    private readonly Guid _userId = Guid.NewGuid();
    private readonly EphemeralDataProtectionProvider _provider = new();
    private readonly InMemoryUserEncryptionKeyRepository _keyRepository = new();

    private TwoFactorSecretProtector CreateProtector() =>
        new(new PerUserCryptoService(
                _keyRepository,
                _provider,
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<ILogger<PerUserCryptoService>>().Object),
            _provider,
            new Mock<ILogger<TwoFactorSecretProtector>>().Object);

    [Fact]
    public async Task ProtectAsync_ProducesPerUserCiphertextDifferentFromPlaintext()
    {
        var protector = CreateProtector();

        var protectedValue = await protector.ProtectAsync(_userId, Secret, CancellationToken.None);

        protectedValue.Should().StartWith("v2:");
        protectedValue.Should().NotContain(Secret);
    }

    [Fact]
    public async Task ProtectThenUnprotect_RoundTripsToOriginalSecret()
    {
        var protector = CreateProtector();

        var protectedValue = await protector.ProtectAsync(_userId, Secret, CancellationToken.None);
        var roundTripped = await protector.UnprotectAsync(_userId, protectedValue, CancellationToken.None);

        roundTripped.Should().Be(Secret);
    }

    [Fact]
    public async Task UnprotectAsync_LegacyV1Payload_StillDecrypts()
    {
        // A row written by the previous app-level protector must keep working
        // until the one-time migration (or the next write) upgrades it to v2.
        var v1Payload = _provider.CreateProtector("TwoFactorAuth.SecretKey.v1").Protect(Secret);
        var protector = CreateProtector();

        var roundTripped = await protector.UnprotectAsync(_userId, v1Payload, CancellationToken.None);

        roundTripped.Should().Be(Secret);
    }

    [Fact]
    public async Task UnprotectAsync_LegacyPlaintext_ReturnedAsIs()
    {
        // A value written before encryption existed cannot be decrypted; it must
        // be returned unchanged so the existing 2FA user is not locked out.
        var protector = CreateProtector();

        (await protector.UnprotectAsync(_userId, Secret, CancellationToken.None)).Should().Be(Secret);
    }
}
