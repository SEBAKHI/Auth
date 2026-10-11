using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Auth.Domain.Errors;
using Auth.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.MachineKeys;

/// <summary>
/// ES256 key material (OI-128, T5): what a generated pair hands out, and which pasted public keys
/// are stored. A private member is never stored and never accepted.
/// </summary>
public class MachineKeyMaterialTests
{
    private readonly MachineKeyMaterial _material = new();

    private static string Jwk(ECParameters parameters, Action<Dictionary<string, object>>? customize = null)
    {
        var jwk = new Dictionary<string, object>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.EncodeToString(parameters.Q.X),
            ["y"] = Base64Url.EncodeToString(parameters.Q.Y),
        };
        customize?.Invoke(jwk);
        return JsonSerializer.Serialize(jwk);
    }

    private static ECParameters NewP256(bool includePrivate = false)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportParameters(includePrivate);
    }

    // --- Generate ---

    [Fact]
    public void Generate_StoresThePublicHalfOnly()
    {
        var generated = _material.Generate();

        using var stored = JsonDocument.Parse(generated.PublicKey.PublicKeyJwk);
        stored.RootElement.EnumerateObject().Select(member => member.Name)
            .Should().Equal("crv", "kty", "x", "y");
        stored.RootElement.TryGetProperty("d", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_ReturnsAPkcs8PrivateKeyThatSignsForTheStoredPublicKey()
    {
        var generated = _material.Generate();

        generated.PrivateKeyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(generated.PrivateKeyPem);
        var data = "assertion"u8.ToArray();
        var signature = privateKey.SignData(data, HashAlgorithmName.SHA256);

        using var publicKey = MachineKeyMaterial.PublicKeyFrom(generated.PublicKey.PublicKeyJwk);
        publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [Fact]
    public void Generate_TwoCalls_TwoKeys()
    {
        _material.Generate().PublicKey.Kid.Should().NotBe(_material.Generate().PublicKey.Kid);
    }

    // --- ImportPublicJwk: what is accepted ---

    [Fact]
    public void Import_APublicP256Jwk_IsStoredCanonically_WithItsRfc7638Thumbprint()
    {
        var parameters = NewP256();
        var pasted = Jwk(parameters, jwk =>
        {
            jwk["kid"] = "my-own-label";
            jwk["use"] = "sig";
            jwk["alg"] = "ES256";
        });

        var imported = _material.ImportPublicJwk(pasted);

        imported.IsError.Should().BeFalse();
        imported.Value.PublicKeyJwk.Should().Be(
            $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64Url.EncodeToString(parameters.Q.X)}\",\"y\":\"{Base64Url.EncodeToString(parameters.Q.Y)}\"}}");

        // The thumbprint as an independent implementation computes it, not the pasted kid.
        var reference = Base64UrlEncoder.Encode(new JsonWebKey(imported.Value.PublicKeyJwk).ComputeJwkThumbprint());
        imported.Value.Kid.Should().Be(reference);
    }

    [Fact]
    public void Import_AWebCryptoExport_IsAccepted_AndStoredWithoutItsExtras()
    {
        // crypto.subtle.exportKey("jwk", publicKey) writes exactly this, members sorted.
        var parameters = NewP256();
        var x = Base64Url.EncodeToString(parameters.Q.X);
        var y = Base64Url.EncodeToString(parameters.Q.Y);
        var webCrypto = $"{{\"crv\":\"P-256\",\"ext\":true,\"key_ops\":[\"verify\"],\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";

        var imported = _material.ImportPublicJwk(webCrypto);

        imported.IsError.Should().BeFalse();
        using var stored = JsonDocument.Parse(imported.Value.PublicKeyJwk);
        stored.RootElement.EnumerateObject().Select(member => member.Name).Should().Equal("crv", "kty", "x", "y");
        imported.Value.Kid.Should().Be(_material.ImportPublicJwk(Jwk(parameters)).Value.Kid, "the same key, the same kid");
    }

    // --- ImportPublicJwk: what is refused ---

    public static TheoryData<string, string> Refused()
    {
        var p256 = NewP256(includePrivate: true);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var p384Parameters = p384.ExportParameters(false);
        using var rsa = RSA.Create(2048);
        var rsaParameters = rsa.ExportParameters(false);
        var offCurve = NewP256();
        offCurve.Q.Y![^1] ^= 0x01;

        return new TheoryData<string, string>
        {
            { "a private key (d)", Jwk(p256, jwk => jwk["d"] = Base64Url.EncodeToString(p256.D)) },
            { "P-384", Jwk(p384Parameters, jwk => jwk["crv"] = "P-384") },
            { "RSA", JsonSerializer.Serialize(new { kty = "RSA", n = Base64Url.EncodeToString(rsaParameters.Modulus), e = Base64Url.EncodeToString(rsaParameters.Exponent) }) },
            { "a symmetric key", JsonSerializer.Serialize(new { kty = "oct", k = "c2VjcmV0" }) },
            { "no y", Jwk(p256, jwk => jwk.Remove("y")) },
            { "a short x", Jwk(p256, jwk => jwk["x"] = "AAAA") },
            { "a point off the curve", Jwk(offCurve) },
            { "an unknown member", Jwk(p256, jwk => jwk["x5c"] = "MIIB") },
            { "key_ops sign", Jwk(p256, jwk => jwk["key_ops"] = new[] { "sign" }) },
            { "key_ops verify and sign", Jwk(p256, jwk => jwk["key_ops"] = new[] { "verify", "sign" }) },
            { "key_ops as a string", Jwk(p256, jwk => jwk["key_ops"] = "verify") },
            { "ext as a string", Jwk(p256, jwk => jwk["ext"] = "true") },
            { "alg other than ES256", Jwk(p256, jwk => jwk["alg"] = "ES384") },
            { "use other than sig", Jwk(p256, jwk => jwk["use"] = "enc") },
            { "a number for x", Jwk(p256, jwk => jwk["x"] = 12) },
            { "not an object", "[\"EC\"]" },
            { "not JSON", "-----BEGIN PUBLIC KEY-----" },
            { "longer than the column", Jwk(p256, jwk => jwk["kid"] = new string('k', 1000)) },
            { "a repeated member", "{\"kty\":\"EC\",\"crv\":\"P-256\",\"crv\":\"P-256\",\"x\":\"" + Base64Url.EncodeToString(p256.Q.X) + "\",\"y\":\"" + Base64Url.EncodeToString(p256.Q.Y) + "\"}" },
        };
    }

    [Fact]
    public void Refused_IsNotEmpty() => Refused().Count.Should().BeGreaterThan(10);

    [Theory]
    [MemberData(nameof(Refused))]
    public void Import_IsRefusedAsInvalidPublicKey(string because, string jwk)
    {
        var imported = _material.ImportPublicJwk(jwk);

        imported.IsError.Should().BeTrue(because);
        imported.FirstError.Code.Should().Be(MachineKeyErrors.InvalidPublicKey.Code, because);
    }

    [Fact]
    public void IsOnP256_HoldsForGeneratedPoints_AndFailsForAnyNeighbour()
    {
        // The curve equation itself, not the platform's import, decides: the same answer on every
        // operating system the API may run on.
        for (var i = 0; i < 20; i++)
        {
            var point = NewP256().Q;
            MachineKeyMaterial.IsOnP256(point.X!, point.Y!).Should().BeTrue();

            var y = (byte[])point.Y!.Clone();
            y[^1] ^= 0x01;
            MachineKeyMaterial.IsOnP256(point.X!, y).Should().BeFalse();
        }
    }

    [Fact]
    public void IsOnP256_ACoordinateOutsideTheField_IsRefused()
    {
        var point = NewP256().Q;
        var outside = Enumerable.Repeat((byte)0xFF, 32).ToArray();

        MachineKeyMaterial.IsOnP256(outside, point.Y!).Should().BeFalse();
        MachineKeyMaterial.IsOnP256(point.X!, outside).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_Nothing_IsRefused(string jwk)
    {
        _material.ImportPublicJwk(jwk).FirstError.Code.Should().Be(MachineKeyErrors.InvalidPublicKey.Code);
    }
}
