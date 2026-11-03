using System.Security.Cryptography;
using Auth_API.Common;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// X03: the signing key and the gateway token are checked in the "key material validation"
/// startup step, beside S11's HMAC keyring.
/// </summary>
public class KeyMaterialValidatorTests
{
    private const string GatewayToken = "a-gateway-token-of-forty-four-characters-000";

    private static readonly IDataProtectionProvider DataProtection = new EphemeralDataProtectionProvider();

    [Fact]
    public void Validate_ValidMaterial_Passes()
    {
        var act = () => KeyMaterialValidator.Validate(Configuration(), DataProtection, isDevelopment: false, plainText: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_APathWithNoFileOutsideDevelopment_RefusesNamingIt()
    {
        var configuration = Configuration(("Jwt:PrivateKeyPath", @"C:\keys\missing-signing-key.pem"));

        var act = () => KeyMaterialValidator.Validate(configuration, DataProtection, isDevelopment: false, plainText: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:PrivateKeyPath is set to *missing-signing-key.pem', but no file exists there*",
                "outside Development a configured key file is never silently replaced by the next source");
    }

    [Fact]
    public void Validate_APathWithNoFileInDevelopment_UsesTheNextSource()
    {
        var configuration = Configuration(("Jwt:PrivateKeyPath", @"C:\keys\missing-signing-key.pem"));

        var act = () => KeyMaterialValidator.Validate(configuration, DataProtection, isDevelopment: true, plainText: false);

        act.Should().NotThrow("Development keeps today's fall-through");
    }

    [Fact]
    public void Validate_APublicOnlySigningKey_Refuses()
    {
        using var rsa = RSA.Create(2048);
        var configuration = Configuration(("Jwt:PrivateKeyPem", rsa.ExportSubjectPublicKeyInfoPem()));

        var act = () => KeyMaterialValidator.Validate(configuration, DataProtection, isDevelopment: false, plainText: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:PrivateKeyPem cannot sign tokens*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("fifteen-chars-x")]
    public void Validate_AShortGatewayToken_RefusesNamingIt(string token)
    {
        var configuration = Configuration(("Gateway:ExpectedToken", token));

        var act = () => KeyMaterialValidator.Validate(configuration, DataProtection, isDevelopment: false, plainText: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Gateway:ExpectedToken is unusable*at least 16 characters*",
                "the import endpoint refuses such a token, and boot now does too");
    }

    [Fact]
    public void Validate_AGatewayTokenOfSixteenCharacters_Passes()
    {
        var configuration = Configuration(("Gateway:ExpectedToken", "sixteen-chars-xx"));

        var act = () => KeyMaterialValidator.Validate(configuration, DataProtection, isDevelopment: false, plainText: false);

        act.Should().NotThrow();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:PrivateKeyPem"] = TestSigningKeys.NewPrivateKeyPem(),
            ["Gateway:ExpectedToken"] = GatewayToken
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
