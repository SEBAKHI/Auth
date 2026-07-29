using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for the Apple ID-token validation: signature against a (mock)
/// JWKS, issuer, audience (Services ID), lifetime and nonce; Apple's
/// name-less tokens map to empty names.
/// </summary>
public class AppleAuthProviderTests
{
    private const string ServicesId = "com.example.accounts";
    private const string KeyId = "test-key";

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private AppleAuthProvider CreateProvider(bool enabled = true)
    {
        var parameters = _signingKey.ExportParameters(false);
        var jwks =
            $"{{\"keys\":[{{\"kty\":\"EC\",\"kid\":\"{KeyId}\",\"use\":\"sig\",\"alg\":\"ES256\",\"crv\":\"P-256\"," +
            $"\"x\":\"{Base64UrlEncoder.Encode(parameters.Q.X!)}\",\"y\":\"{Base64UrlEncoder.Encode(parameters.Q.Y!)}\"}}]}}";

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwks)
        });

        return new AppleAuthProvider(
            new HttpClient(handler),
            Options.Create(new ExternalAuthSettings
            {
                Apple = new AppleAuthSettings { Enabled = enabled, ServicesId = ServicesId }
            }),
            new Mock<ILogger<AppleAuthProvider>>().Object);
    }

    private string CreateIdToken(
        string audience = ServicesId,
        string? nonce = null,
        DateTime? expires = null,
        string email = "relay@privaterelay.appleid.com",
        string emailVerified = "true")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "apple-sub-001"),
            new(JwtRegisteredClaimNames.Email, email),
            new("email_verified", emailVerified)
        };
        if (nonce is not null)
        {
            claims.Add(new Claim("nonce", nonce));
        }

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);
        var token = new JwtSecurityToken(
            issuer: AppleAuthProvider.AppleIssuer,
            audience: audience,
            claims: claims,
            notBefore: expiresAt.AddMinutes(-15),
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(_signingKey) { KeyId = KeyId },
                SecurityAlgorithms.EcdsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsUserInfoWithoutNames()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(CreateIdToken(), null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ProviderUserId.Should().Be("apple-sub-001");
        result.Value.Email.Should().Be("relay@privaterelay.appleid.com");
        result.Value.EmailVerified.Should().BeTrue();
        result.Value.FirstName.Should().BeEmpty("Apple never puts the name in the ID token");
        result.Value.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_MatchingNonce_Succeeds()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(
            CreateIdToken(nonce: "expected-nonce"), "expected-nonce", CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_NonceMismatch_FailsClosed()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(
            CreateIdToken(nonce: "other"), "expected-nonce", CancellationToken.None);

        result.FirstError.Code.Should().Be("ExternalAuth.TokenVerificationFailed");
    }

    [Fact]
    public async Task ValidateTokenAsync_WrongAudience_FailsClosed()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(
            CreateIdToken(audience: "com.attacker.app"), null, CancellationToken.None);

        result.FirstError.Code.Should().Be("ExternalAuth.TokenVerificationFailed");
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_FailsClosed()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(
            CreateIdToken(expires: DateTime.UtcNow.AddMinutes(-10)), null, CancellationToken.None);

        result.FirstError.Code.Should().Be("ExternalAuth.TokenVerificationFailed");
    }

    [Fact]
    public async Task ValidateTokenAsync_UnverifiedEmail_IsReportedAsUnverified()
    {
        var provider = CreateProvider();

        var result = await provider.ValidateTokenAsync(
            CreateIdToken(emailVerified: "false"), null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailVerified.Should().BeFalse("the login handler rejects unverified provider emails");
    }

    [Fact]
    public async Task ValidateTokenAsync_ProviderDisabled_ReturnsNotConfigured()
    {
        var provider = CreateProvider(enabled: false);

        var result = await provider.ValidateTokenAsync(CreateIdToken(), null, CancellationToken.None);

        result.FirstError.Code.Should().Be("ExternalAuth.ProviderNotConfigured");
    }
}
