using Auth.Application.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Unit tests for IdentityProviderSettings.ResolvePublicBaseUrl — the guard that
/// keeps public URLs (authorize returnTo, discovery endpoints) built from the
/// configured public origin rather than the internal Request.Host behind a proxy.
/// </summary>
public class IdentityProviderSettingsTests
{
    private const string RequestFallback = "https://identity.example.com"; // internal host

    [Fact]
    public void ResolvePublicBaseUrl_WhenConfigured_UsesConfiguredOrigin()
    {
        var settings = new IdentityProviderSettings { PublicBaseUrl = "https://auth.example.com" };

        settings.ResolvePublicBaseUrl(RequestFallback).Should().Be("https://auth.example.com");
    }

    [Fact]
    public void ResolvePublicBaseUrl_TrimsTrailingSlash()
    {
        var settings = new IdentityProviderSettings { PublicBaseUrl = "https://auth.example.com/" };

        settings.ResolvePublicBaseUrl(RequestFallback).Should().Be("https://auth.example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePublicBaseUrl_WhenNotConfigured_FallsBackToRequestHost(string configured)
    {
        // Proxy-less dev: no configured public origin, so the request host (which
        // is already public there) is used.
        var settings = new IdentityProviderSettings { PublicBaseUrl = configured };

        settings.ResolvePublicBaseUrl("http://localhost:5100").Should().Be("http://localhost:5100");
    }
}
