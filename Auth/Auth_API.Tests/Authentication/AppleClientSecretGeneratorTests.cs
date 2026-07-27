using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for the ES256 client secret Apple requires on its OAuth
/// endpoints: issuer = Team ID, subject = Services ID, audience = Apple,
/// kid header from the .p8 key.
/// </summary>
public class AppleClientSecretGeneratorTests
{
    private static AppleAuthSettings CreateConfiguredApple() => new()
    {
        Enabled = true,
        ServicesId = "com.example.accounts",
        TeamId = "TEAM123456",
        KeyId = "KEY9876543",
        PrivateKeyPem = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()
    };

    [Fact]
    public void Generate_ProducesAValidEs256ClientSecret()
    {
        var apple = CreateConfiguredApple();
        var generator = new AppleClientSecretGenerator(
            Options.Create(new ExternalAuthSettings { Apple = apple }));

        var secret = generator.Generate();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(secret);
        jwt.Issuer.Should().Be(apple.TeamId);
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("https://appleid.apple.com");
        jwt.Subject.Should().Be(apple.ServicesId);
        jwt.Header.Kid.Should().Be(apple.KeyId);
        jwt.Header.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Generate_MissingConfiguration_ThrowsWithGuidance()
    {
        var generator = new AppleClientSecretGenerator(
            Options.Create(new ExternalAuthSettings { Apple = new AppleAuthSettings { Enabled = true } }));

        var act = () => generator.Generate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AppleSigningKeyPem*");
    }
}
