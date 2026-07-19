using Auth_API.Common;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.Common;

/// <summary>
/// Unit tests for ProductionSecretGuard — refuses to boot when plaintext
/// crown-jewel secrets are present in the Production configuration.
/// </summary>
public class ProductionSecretGuardTests
{
    private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
    }

    [Fact]
    public void Production_WithPlaintextPrivateKey_Throws()
    {
        var config = BuildConfig(("Jwt:PrivateKeyPem", "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----"));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:PrivateKeyPem*");
    }

    [Fact]
    public void Production_WithPlaintextHmacKey_Throws()
    {
        var config = BuildConfig(("Jwt:RefreshTokenHmacKeyPlain", "somebase64key=="));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Production_WithNoPlaintextSecrets_DoesNotThrow()
    {
        // Encrypted values (PrivateKeyEncrypted) are allowed; only plaintext is forbidden.
        var config = BuildConfig(("Jwt:PrivateKeyEncrypted", "CfDJ8-encrypted-blob"));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void NonProduction_WithPlaintextSecret_DoesNotThrow()
    {
        // Development legitimately uses plaintext keys.
        var config = BuildConfig(("Jwt:PrivateKeyPem", "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----"));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: false);

        act.Should().NotThrow();
    }
}
