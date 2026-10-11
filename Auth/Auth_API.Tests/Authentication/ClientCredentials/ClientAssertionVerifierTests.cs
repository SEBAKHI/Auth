using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// The client assertion rules of the client_credentials grant (OI-128, T2): one test per rule, each
/// with a real assertion signed by a real P-256 key, verified by the real verifier. The handler
/// answers every refusal here with the same Auth.InvalidClient (see the handler tests); these tests
/// pin which assertions are refused at all.
/// </summary>
public sealed class ClientAssertionVerifierTests : IDisposable
{
    private readonly MachineCredentials _credentials = new();
    private readonly ClientAssertionVerifier _verifier = new(TimeProvider.System);

    public void Dispose() => _credentials.Dispose();

    private Task<ClientAssertionVerification> VerifyAsync(string assertion) =>
        _verifier.VerifyAsync(assertion, _credentials.PublicKey.PublicKeyJwk, MachineCredentials.Rules);

    private async Task AssertRefusedAsync(string assertion)
    {
        var verification = await VerifyAsync(assertion);
        verification.IsValid.Should().BeFalse();
        verification.Jti.Should().BeNull("a refused assertion yields nothing to record");
        verification.FailureReason.Should().NotBeNullOrWhiteSpace("the log must say which rule failed");
    }

    // --- what passes ---

    [Fact]
    public async Task Verify_TheSdkShape_IsAccepted_WithItsJtiAndExpiry()
    {
        string? jti = null;
        var assertion = _credentials.Assertion(d => jti = (string)d.Claims["jti"]);

        var verification = await VerifyAsync(assertion);

        verification.IsValid.Should().BeTrue(verification.FailureReason);
        verification.Jti.Should().Be(jti);
        verification.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(60), TimeSpan.FromSeconds(5));
        verification.ExpiresAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Verify_TheIssuerAsAnArrayOfOne_IsAccepted()
    {
        var assertion = _credentials.Assertion(d =>
        {
            d.Audience = null;
            d.Claims["aud"] = new[] { MachineCredentials.Issuer };
        });

        (await VerifyAsync(assertion)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("client-authentication+jwt")]
    [InlineData("Client-Authentication+JWT")]
    public async Task Verify_AnAcceptedType_IsAccepted(string type)
    {
        (await VerifyAsync(_credentials.Assertion(d => d.TokenType = type))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_AnUntypedAssertion_IsAccepted()
    {
        // rfc7523bis: servers should not reject an assertion without typ.
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "ES256", ["kid"] = _credentials.Kid },
            MachineCredentials.ValidPayload(),
            _credentials.SignEs256);

        (await VerifyAsync(assertion)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_AnIatWithinTheSkew_IsAccepted()
    {
        var assertion = _credentials.Assertion(d => d.IssuedAt = DateTime.UtcNow.AddSeconds(30));

        (await VerifyAsync(assertion)).IsValid.Should().BeTrue();
    }

    // --- alg: exactly ES256 ---

    [Fact]
    public async Task Verify_AlgNone_IsRefused()
    {
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "none", ["kid"] = _credentials.Kid, ["typ"] = "JWT" },
            MachineCredentials.ValidPayload(),
            sign: null);

        await AssertRefusedAsync(assertion);
    }

    [Fact]
    public async Task Verify_Hs256KeyedWithThePublicKey_IsRefused()
    {
        // The classic algorithm confusion: a MAC whose secret is the server's own public key, which
        // every attacker has. Tried with both forms an attacker would reach for.
        foreach (var secret in new[]
                 {
                     Encoding.UTF8.GetBytes(_credentials.PublicKey.PublicKeyJwk),
                     _credentials.PrivateKey.ExportSubjectPublicKeyInfo()
                 })
        {
            var assertion = MachineCredentials.Raw(
                new Dictionary<string, object> { ["alg"] = "HS256", ["kid"] = _credentials.Kid, ["typ"] = "JWT" },
                MachineCredentials.ValidPayload(),
                input => HMACSHA256.HashData(secret, input));

            await AssertRefusedAsync(assertion);
        }
    }

    [Theory]
    [InlineData("ES384")]
    [InlineData("ES512")]
    public async Task Verify_AnotherEcdsaAlgorithmUnderTheSameP256Key_IsRefusedByTheEs256Pin(string alg)
    {
        // F3: the one case where only the ES256 pin can refuse: a valid ECDSA signature by the
        // registered P-256 key itself, hashed and labelled as another ECDSA algorithm. none, HMAC
        // and RSA above are refused by the key type too, so they cannot prove the pin.
        var hash = alg == "ES384" ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA512;
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = alg, ["kid"] = _credentials.Kid, ["typ"] = "JWT" },
            MachineCredentials.ValidPayload(),
            input => _credentials.PrivateKey.SignData(input, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        var verification = await VerifyAsync(assertion);

        verification.IsValid.Should().BeFalse();
        verification.FailureReason.Should().Be(nameof(SecurityTokenSignatureKeyNotFoundException),
            "the pin leaves no key-and-algorithm pair that can check the signature");
    }

    [Fact]
    public async Task Verify_Rs256_IsRefused()
    {
        using var rsa = RSA.Create(2048);
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "RS256", ["kid"] = _credentials.Kid, ["typ"] = "JWT" },
            MachineCredentials.ValidPayload(),
            input => rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        await AssertRefusedAsync(assertion);
    }

    [Fact]
    public async Task Verify_AnotherKeyUnderTheSameKid_IsRefused()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var assertion = _credentials.Assertion(d => d.SigningCredentials = new SigningCredentials(
            new ECDsaSecurityKey(stranger) { KeyId = _credentials.Kid }, SecurityAlgorithms.EcdsaSha256));

        await AssertRefusedAsync(assertion);
    }

    [Fact]
    public async Task Verify_ATamperedPayload_IsRefused()
    {
        var parts = _credentials.Assertion().Split('.');
        var forged = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "ES256", ["kid"] = _credentials.Kid, ["typ"] = "JWT" },
            MachineCredentials.ValidPayload(),
            sign: null);

        // The original signature on a different payload.
        await AssertRefusedAsync($"{forged.Split('.')[0]}.{forged.Split('.')[1]}.{parts[2]}");
    }

    // --- iss and sub ---

    [Fact]
    public async Task Verify_IssNotSub_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Claims["sub"] = "other-app"));
    }

    [Fact]
    public async Task Verify_IssAndSubOfAnotherApplication_IsRefused()
    {
        // The key belongs to "edis": an assertion that names another client, consistently, is refused.
        await AssertRefusedAsync(_credentials.Assertion(d =>
        {
            d.Issuer = "other-app";
            d.Claims["sub"] = "other-app";
        }));
    }

    [Fact]
    public async Task Verify_IssDifferingOnlyInCase_IsRefused_ByTheIssuerRule()
    {
        // sub stays right, so only the issuer comparison can refuse it.
        var verification = await VerifyAsync(_credentials.Assertion(d => d.Issuer = "EDIS"));

        verification.IsValid.Should().BeFalse();
        verification.FailureReason.Should().Be(nameof(SecurityTokenInvalidIssuerException));
    }

    [Fact]
    public async Task Verify_SubDifferingOnlyInCase_IsRefused_ByTheSubjectRule()
    {
        var verification = await VerifyAsync(_credentials.Assertion(d => d.Claims["sub"] = "EDIS"));

        verification.IsValid.Should().BeFalse();
        verification.FailureReason.Should().Be("sub is not the client");
    }

    // --- aud: the issuer identifier as the sole value ---

    [Fact]
    public async Task Verify_TheTokenEndpointAsAudience_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Audience = MachineCredentials.TokenEndpoint));
    }

    [Fact]
    public async Task Verify_TheIssuerAndASecondAudience_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d =>
        {
            d.Audience = null;
            d.Claims["aud"] = new[] { MachineCredentials.Issuer, "https://other.example.com" };
        }));
    }

    [Fact]
    public async Task Verify_TheIssuerWithATrailingSlash_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Audience = MachineCredentials.Issuer + "/"));
    }

    // --- exp, nbf, iat ---

    [Fact]
    public async Task Verify_NoExp_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Expires = null));
    }

    [Fact]
    public async Task Verify_ExpTenMinutesAhead_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Expires = DateTime.UtcNow.AddMinutes(10)));
    }

    [Fact]
    public async Task Verify_ExpJustInsideFiveMinutesAndTheSkew_IsAccepted()
    {
        // The boundary of the refusal above: five minutes plus the 60-second skew.
        var assertion = _credentials.Assertion(d => d.Expires = DateTime.UtcNow.AddMinutes(5).AddSeconds(50));

        (await VerifyAsync(assertion)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_AnExpiredAssertion_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d =>
        {
            d.IssuedAt = DateTime.UtcNow.AddMinutes(-10);
            d.Expires = DateTime.UtcNow.AddMinutes(-5);
        }));
    }

    [Fact]
    public async Task Verify_AnNbfInTheFuture_IsRefused_AsNotYetValid()
    {
        // exp after nbf, so the refusal is the "not yet valid" rule and not "nbf after exp".
        var verification = await VerifyAsync(_credentials.Assertion(d =>
        {
            d.NotBefore = DateTime.UtcNow.AddMinutes(3);
            d.Expires = DateTime.UtcNow.AddMinutes(4);
        }));

        verification.IsValid.Should().BeFalse();
        verification.FailureReason.Should().Be(nameof(SecurityTokenNotYetValidException));
    }

    [Fact]
    public async Task Verify_AnIatInTheFuture_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.IssuedAt = DateTime.UtcNow.AddMinutes(3)));
    }

    // --- jti ---

    [Fact]
    public async Task Verify_NoJti_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Claims.Remove("jti")));
    }

    [Fact]
    public async Task Verify_AJtiLongerThanTheColumn_IsRefused()
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.Claims["jti"] = new string('j', 201)));
    }

    // --- typ ---

    [Theory]
    [InlineData("at+jwt")]
    [InlineData("id+jwt")]
    [InlineData("application/jwt")]
    public async Task Verify_AnUnknownType_IsRefused(string type)
    {
        await AssertRefusedAsync(_credentials.Assertion(d => d.TokenType = type));
    }

    // --- shape ---

    [Fact]
    public async Task Verify_ACritHeader_IsRefused()
    {
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object>
            {
                ["alg"] = "ES256", ["kid"] = _credentials.Kid, ["typ"] = "JWT", ["crit"] = new[] { "exp-ext" }, ["exp-ext"] = 1
            },
            MachineCredentials.ValidPayload(),
            _credentials.SignEs256);

        await AssertRefusedAsync(assertion);
    }

    [Fact]
    public async Task Verify_AnAssertionLongerThanTheCap_IsRefusedUnread()
    {
        var assertion = _credentials.Assertion(d => d.Claims["pad"] = new string('p', ClientAssertionVerifier.MaxAssertionLength));

        await AssertRefusedAsync(assertion);
        _verifier.ReadKeyId(assertion).Should().BeNull();
    }

    // --- the kid, read before anything is verified ---

    [Fact]
    public void ReadKeyId_ReturnsTheHeaderKid()
    {
        _verifier.ReadKeyId(_credentials.Assertion()).Should().Be(_credentials.Kid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("aaa.bbb.ccc")]
    [InlineData("a.b")]
    public void ReadKeyId_NotAJws_ReturnsNull(string assertion)
    {
        _verifier.ReadKeyId(assertion).Should().BeNull();
    }

    [Fact]
    public void ReadKeyId_NoKid_ReturnsNull()
    {
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT" },
            MachineCredentials.ValidPayload(),
            _credentials.SignEs256);

        _verifier.ReadKeyId(assertion).Should().BeNull();
    }

    [Theory]
    [InlineData("kid\r\nforged log line")]
    [InlineData("kid with spaces")]
    [InlineData("kid/../x")]
    [InlineData("kid=")]
    [InlineData("ÉDIS")]
    public void ReadKeyId_AKidOutsideTheBase64UrlShape_ReturnsNull(string kid)
    {
        // An anonymous caller's unsigned header: never looked up, never logged as-is.
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT", ["kid"] = kid },
            MachineCredentials.ValidPayload(),
            _credentials.SignEs256);

        _verifier.ReadKeyId(assertion).Should().BeNull();
    }

    [Fact]
    public void ReadKeyId_AKidLongerThanTheColumn_ReturnsNull()
    {
        var assertion = MachineCredentials.Raw(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT", ["kid"] = new string('k', 65) },
            MachineCredentials.ValidPayload(),
            _credentials.SignEs256);

        _verifier.ReadKeyId(assertion).Should().BeNull();
    }

    [Fact]
    public async Task Verify_TheSameKeyTwice_StillVerifies()
    {
        // The per-request key is disposed after each verification; a cached signature provider
        // holding it would fail the second assertion under the same key.
        (await VerifyAsync(_credentials.Assertion())).IsValid.Should().BeTrue();
        (await VerifyAsync(_credentials.Assertion())).IsValid.Should().BeTrue();
    }
}
