using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// A real machine key for the OI-128 tests: a P-256 pair generated here, its public half in the
/// stored form the key-material service produces, and real assertions signed with it. For the
/// shapes a well-behaved signer never produces (alg none, HMAC, RSA, no typ), the token is built
/// by hand from header and payload JSON.
/// </summary>
public sealed class MachineCredentials : IDisposable
{
    public const string Issuer = "https://auth.example.com";
    public const string TokenEndpoint = "https://auth.example.com/api/v1/auth/token";
    public const string ClientId = "shop";

    public MachineCredentials()
    {
        PrivateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var point = PrivateKey.ExportParameters(includePrivateParameters: false).Q;
        var jwk = JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.EncodeToString(point.X),
            y = Base64Url.EncodeToString(point.Y)
        });

        var imported = new MachineKeyMaterial().ImportPublicJwk(jwk);
        imported.IsError.Should().BeFalse("a freshly generated P-256 public key must import");
        PublicKey = imported.Value;
    }

    public ECDsa PrivateKey { get; }

    /// <summary>The stored form of the public key, and its kid.</summary>
    public MachinePublicKey PublicKey { get; }

    public string Kid => PublicKey.Kid;

    public static ClientAssertionRules Rules => new(ClientId, Issuer, TimeSpan.FromSeconds(60));

    /// <summary>
    /// A valid assertion, as the SDK signs one, after <paramref name="customize"/> changed its
    /// descriptor.
    /// </summary>
    public string Assertion(Action<SecurityTokenDescriptor>? customize = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            TokenType = "client-authentication+jwt",
            Issuer = ClientId,
            Audience = Issuer,
            IssuedAt = now,
            Expires = now.AddSeconds(60),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = ClientId,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(PrivateKey) { KeyId = Kid }, SecurityAlgorithms.EcdsaSha256)
        };
        customize?.Invoke(descriptor);

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>The claims of a valid assertion, for the hand-built shapes.</summary>
    public static Dictionary<string, object> ValidPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new Dictionary<string, object>
        {
            ["iss"] = ClientId,
            ["sub"] = ClientId,
            ["aud"] = Issuer,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now,
            ["exp"] = now + 60
        };
    }

    /// <summary>
    /// A compact JWS built by hand: <paramref name="header"/> and <paramref name="payload"/> as
    /// JSON, and the signature <paramref name="sign"/> computes over the signing input (none when
    /// it is null).
    /// </summary>
    public static string Raw(object header, object payload, Func<byte[], byte[]>? sign)
    {
        var signingInput =
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}." +
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var signature = sign is null ? string.Empty : Base64Url.EncodeToString(sign(Encoding.ASCII.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    /// <summary>An ES256 signature with this key over <paramref name="input"/>, in JWS (r||s) form.</summary>
    public byte[] SignEs256(byte[] input) =>
        PrivateKey.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => PrivateKey.Dispose();
}
