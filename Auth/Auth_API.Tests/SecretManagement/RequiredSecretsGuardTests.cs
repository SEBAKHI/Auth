using Auth.Shared.Configuration;
using Auth_API.Common;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// The startup guard that converts "missing secret" from a production-only 500
/// on an end-user request into a boot failure naming the key.
/// </summary>
public class RequiredSecretsGuardTests
{
    [Fact]
    public void Passes_WhenEveryRequiredSecretIsPresent()
    {
        var act = () => RequiredSecretsGuard.EnsureAllPresent(BuildConfiguration(), "Certificate");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("AccountDeletion:IdentifierHmacKeyPlain")]
    [InlineData("Jwt:PrivateKeyPem")]
    [InlineData("Jwt:RefreshTokenHmacKeyPlain")]
    [InlineData("Gateway:ExpectedToken")]
    public void Throws_NamingTheMissingSecret(string missingKey)
    {
        var configuration = BuildConfiguration(omit: missingKey);

        var act = () => RequiredSecretsGuard.EnsureAllPresent(configuration, "Certificate");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{missingKey}*",
                "the failure message must name the key so an operator can act on it without reading code")
            .And.Message.Should().Contain("Certificate",
                "the storage mode determines where the secret should have come from");
    }

    [Fact]
    public void Throws_WhenASecretIsPresentButBlank()
    {
        var configuration = BuildConfiguration(blank: "Jwt:PrivateKeyPem");

        var act = () => RequiredSecretsGuard.EnsureAllPresent(configuration, "PlainText");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:PrivateKeyPem*",
                "an empty string is an absent secret; treating it as present is how the original bug " +
                "reached production");
    }

    [Fact]
    public void Throws_ListingEverySecretThatIsMissing_NotJustTheFirst()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => RequiredSecretsGuard.EnsureAllPresent(configuration, "Certificate");

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        foreach (var secret in RequiredSecretsRegistry.All)
        {
            message.Should().Contain(secret.ConfigurationKey,
                "an operator fixing one key at a time across restarts is a wasted deployment each round");
        }
    }

    private static IConfiguration BuildConfiguration(string? omit = null, string? blank = null)
    {
        var values = new Dictionary<string, string?>();

        foreach (var secret in RequiredSecretsRegistry.All)
        {
            if (secret.ConfigurationKey == omit)
            {
                continue;
            }

            values[secret.ConfigurationKey] = secret.ConfigurationKey == blank ? "  " : "provisioned";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
