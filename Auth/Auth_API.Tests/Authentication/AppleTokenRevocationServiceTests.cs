using System.Net;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for Apple's token lifecycle: best-effort authorization-code
/// exchange (never throws) and refresh-token revocation.
/// </summary>
public class AppleTokenRevocationServiceTests
{
    private static TestHelpers.TestOptions<ExternalAuthSettings> CreateSettings() => TestHelpers.CreateOptions(new ExternalAuthSettings
    {
        Apple = new AppleAuthSettings
        {
            Enabled = true,
            ServicesId = "com.example.accounts",
            TeamId = "TEAM123456",
            KeyId = "KEY9876543",
            PrivateKeyPem = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()
        }
    });

    private static AppleTokenRevocationService CreateService(
        FakeHttpMessageHandler handler, TestHelpers.TestOptions<ExternalAuthSettings>? settings = null)
    {
        settings ??= CreateSettings();
        return new AppleTokenRevocationService(
            new HttpClient(handler),
            new AppleClientSecretGenerator(settings),
            settings,
            new Mock<ILogger<AppleTokenRevocationService>>().Object);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Success_ReturnsTheRefreshToken()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"at\",\"refresh_token\":\"rt-123\"}")
        });
        var service = CreateService(handler);

        var token = await service.ExchangeCodeAsync("auth-code", CancellationToken.None);

        token.Should().Be("rt-123");
        handler.Requests.Should().ContainSingle()
            .Which.RequestUri!.AbsoluteUri.Should().Be("https://appleid.apple.com/auth/token");
    }

    [Fact]
    public async Task ExchangeCodeAsync_ProviderRejects_ReturnsNullWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}")
        });
        var service = CreateService(handler);

        var token = await service.ExchangeCodeAsync("expired-code", CancellationToken.None);

        token.Should().BeNull("sign-in must never break over a failed exchange");
    }

    [Fact]
    public async Task RevokeAsync_Success_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler);

        var revoked = await service.RevokeAsync("rt-123", CancellationToken.None);

        revoked.Should().BeTrue();
        handler.Requests.Should().ContainSingle()
            .Which.RequestUri!.AbsoluteUri.Should().Be("https://appleid.apple.com/auth/revoke");
    }

    [Fact]
    public async Task RevokeAsync_ProviderRejects_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var service = CreateService(handler);

        var revoked = await service.RevokeAsync("rt-123", CancellationToken.None);

        revoked.Should().BeFalse();
    }
}
