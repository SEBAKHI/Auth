using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="GoogleAuthProvider"/>.
/// </summary>
/// <remarks>
/// Google is the primary provider and had no tests at all, while Apple — the
/// secondary one — was covered. That asymmetry is what this file corrects.
/// <para>
/// COVERAGE LIMIT, stated rather than implied: everything below the
/// configuration guard is not reachable from a unit test. The signature check
/// calls the static <c>GoogleJsonWebSignature.ValidateAsync</c>, which fetches
/// Google's live signing keys over the network — it cannot be substituted
/// without introducing a seam. So the paths that matter most (a valid token, an
/// audience meant for another client, an expired token, a mismatched nonce) stay
/// unverified here. Closing that properly means putting an injectable validator
/// behind an interface, the way <see cref="AppleAuthProvider"/> ended up with its
/// own JWKS handling; it is a real gap, not a covered one.
/// </para>
/// </remarks>
public class GoogleAuthProviderTests
{
    private static GoogleAuthProvider CreateProvider(GoogleAuthSettings? google)
        => new(
            TestHelpers.CreateOptions(new ExternalAuthSettings { Google = google }),
            new Mock<ILogger<GoogleAuthProvider>>().Object);

    [Fact]
    public void ProviderName_IsTheFactoryKey()
    {
        // The factory resolves providers by this string; a change here silently
        // makes every Google sign-in return "provider not supported".
        CreateProvider(null).ProviderName.Should().Be("google");
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenGoogleIsNotConfiguredAtAll_ReturnsNotConfigured()
    {
        var result = await CreateProvider(null)
            .ValidateTokenAsync("any-token", null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ExternalAuth.ProviderNotConfigured");
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenDisabled_ReturnsNotConfigured()
    {
        // The kill switch has to hold even with a client id present, or turning
        // Google off in the console would leave the endpoint still accepting
        // tokens for it.
        var result = await CreateProvider(new GoogleAuthSettings
        {
            Enabled = false,
            ClientId = "client-id.apps.googleusercontent.com"
        }).ValidateTokenAsync("any-token", null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ExternalAuth.ProviderNotConfigured");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ValidateTokenAsync_WithoutAClientId_ReturnsNotConfigured(string? clientId)
    {
        // The client id IS the audience check. Enabled with none configured would
        // otherwise hand an empty audience to the validator, so this must refuse
        // rather than proceed.
        var result = await CreateProvider(new GoogleAuthSettings
        {
            Enabled = true,
            ClientId = clientId!
        }).ValidateTokenAsync("any-token", null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ExternalAuth.ProviderNotConfigured");
    }

    [Fact]
    public async Task ValidateTokenAsync_ChecksConfigurationBeforeTouchingTheToken()
    {
        // Proven by the absence of a network call: a syntactically impossible
        // token still returns the configuration error rather than a verification
        // failure, so nothing reaches Google's key endpoint while disabled.
        var result = await CreateProvider(new GoogleAuthSettings { Enabled = false })
            .ValidateTokenAsync("not.a.jwt", "some-nonce", CancellationToken.None);

        result.FirstError.Code.Should().Be("ExternalAuth.ProviderNotConfigured");
    }
}
