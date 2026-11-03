using System.Security.Cryptography;
using Auth.Application.Security.Keyring;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S12 TS4 (KR2): every new signing key is named by its RFC 7638 JWK thumbprint, base64url.
/// </summary>
public class SigningKeyThumbprintTests
{
    // RFC 7638 §3.1: the example RSA key and its thumbprint.
    private const string Modulus =
        "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw";

    private const string Exponent = "AQAB";

    private const string Rfc7638Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    [Fact]
    public void TheRfc7638Example_GivesItsPublishedThumbprint()
    {
        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(Modulus),
            Exponent = Base64UrlEncoder.DecodeBytes(Exponent)
        });

        SigningKeyThumbprint.Of(rsa).Should().Be(Rfc7638Thumbprint);
    }

    [Fact]
    public void APrivateKeyAndItsPublicKey_HaveOneThumbprint()
    {
        using var rsa = RSA.Create(2048);

        SigningKeyThumbprint.OfPem(rsa.ExportPkcs8PrivateKeyPem())
            .Should().Be(SigningKeyThumbprint.OfPem(rsa.ExportSubjectPublicKeyInfoPem()))
            .And.Be(SigningKeyThumbprint.Of(rsa));
    }
}
