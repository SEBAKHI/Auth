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
    public void Production_WithPlaintextIdentifierHmacKey_Throws()
    {
        var config = BuildConfig(("AccountDeletion:IdentifierHmacKeyPlain", "somebase64key=="));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AccountDeletion:IdentifierHmacKeyPlain*");
    }

    [Fact]
    public void Production_WithNoPlaintextSecrets_DoesNotThrow()
    {
        // Encrypted values (PrivateKeyEncrypted) are allowed; only plaintext is forbidden.
        var config = BuildConfig(("Jwt:PrivateKeyEncrypted", "CfDJ8-encrypted-blob"));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Keyring:Hmac:Keys:2:Material", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")]
    [InlineData("Keyring:Hmac:CurrentKeyId", "2")]
    [InlineData("Keyring:Rsa:Keys:abc:PrivateKeyPem", "-----BEGIN PRIVATE KEY-----")]
    public void Production_WithAnyKeyringChild_Throws_NamingIt(string key, string value)
    {
        // S11 TS8 (P4-R4): configuration["Keyring:Hmac"] of a full section is null, so a
        // keyring in plaintext settings is only seen child by child.
        var config = BuildConfig((key, value));

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(config, isProduction: true);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void Production_WithTheKeyringSetting_DoesNotThrow()
    {
        // A non-secret key beside the keyring is not key material. (The name is the setting
        // the card once planned, dropped under D-S11B-1; any sibling of Keyring:Hmac would do.)
        var config = BuildConfig(("Keyring:IssueUnderPurposeKeys", "true"), ("Keyring:Hmac:Keys:2:Material", ""));

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
