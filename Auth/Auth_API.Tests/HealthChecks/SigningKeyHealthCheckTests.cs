using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// X03: readiness proves the signing key signs. Until X03 it asked for the public key, which a
/// key holding only its public part still exports, so <c>/ready</c> read Healthy and the first
/// sign-in failed.
/// </summary>
public class SigningKeyHealthCheckTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "https://auth.example.com",
        Audience = "auth-platform",
        KeyId = "test-key"
    };

    [Fact]
    public async Task CheckHealthAsync_APublicOnlyKey_IsUnhealthy()
    {
        using var keyPair = RSA.Create(2048);
        var publicOnly = RSA.Create();
        publicOnly.ImportFromPem(keyPair.ExportSubjectPublicKeyInfoPem());
        using var service = new JwtTokenService(Settings, publicOnly);

        var result = await new SigningKeyHealthCheck(service).CheckHealthAsync(new HealthCheckContext());

        service.GetPublicKeyPem().Should().NotBeNullOrWhiteSpace(
            "this is why asking for the public key proved nothing");
        result.Status.Should().Be(HealthStatus.Unhealthy, "a key that cannot sign cannot issue a single token");
    }

    [Fact]
    public async Task CheckHealthAsync_AKeyThatSigns_IsHealthy()
    {
        using var service = new JwtTokenService(Settings, RSA.Create(2048));

        var result = await new SigningKeyHealthCheck(service).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void ProbeSigningKey_AKeyThatSigns_SignsAndValidates()
    {
        using var service = new JwtTokenService(Settings, RSA.Create(2048));

        service.ProbeSigningKey().IsError.Should().BeFalse();
    }
}
