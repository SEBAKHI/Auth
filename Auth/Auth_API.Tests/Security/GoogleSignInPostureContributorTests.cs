using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Security;

/// <summary>
/// S02 PR D (review of #162): Google offered while its code exchange cannot complete is a
/// Warning at startup and at every reload that causes it, naming what is missing.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the secret check removed; the origins check removed;
/// the warning written at Information; any non-blank origin counted (REVIEW 162 F8.2: http
/// origins only read as Ok).
/// </remarks>
public sealed class GoogleSignInPostureContributorTests
{
    private const string Origin = "https://accounts.example.com";

    private static GoogleSignInPostureContributor Contributor(GoogleAuthSettings? google, params string[] origins)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOptionsSnapshot<ExternalAuthSettings>>(TestHelpers.CreateOptions(new ExternalAuthSettings { Google = google }))
            .AddSingleton<IOptionsSnapshot<IdentityProviderSettings>>(TestHelpers.CreateOptions(new IdentityProviderSettings { FirstPartySpaOrigins = origins }))
            .BuildServiceProvider();
        return new GoogleSignInPostureContributor(services.GetRequiredService<IServiceScopeFactory>());
    }

    private static GoogleAuthSettings On(string? secret) =>
        new() { Enabled = true, ClientId = "test-client.apps.googleusercontent.com", ClientSecret = secret };

    [Fact]
    public async Task On_WithoutTheSecret_IsWeak_AtWarning_NamingTheSecret()
    {
        var posture = await Contributor(On(secret: null), Origin).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        var line = posture.Lines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Arguments.Single()!.ToString().Should().Contain("ExternalAuth:Google:ClientSecret")
            .And.NotContain("FirstPartySpaOrigins");
    }

    [Fact]
    public async Task On_WithoutAFirstPartyOrigin_IsWeak_AtWarning_NamingTheList()
    {
        var posture = await Contributor(On(secret: "s")).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        var line = posture.Lines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Arguments.Single()!.ToString().Should().Contain("IdentityProvider:FirstPartySpaOrigins")
            .And.NotContain("ClientSecret");
    }

    [Fact]
    public async Task On_WithOnlyHttpOrigins_IsWeak_NamingTheList()
    {
        // REVIEW 162 F8.2: the resolver accepts only https origins, so http ones count as none.
        var posture = await Contributor(On(secret: "s"), "http://accounts.example.com", "http://console.example.com")
            .EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Lines.Single().Arguments.Single()!.ToString().Should().Contain("IdentityProvider:FirstPartySpaOrigins");
    }

    [Fact]
    public async Task On_WithSecretAndOrigins_IsOk()
    {
        var posture = await Contributor(On(secret: "s"), Origin, "https://console.example.com").EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().ContainSingle(line => line.Level == LogLevel.Information && line.Arguments.Contains(2));
    }

    [Theory]
    [InlineData(false, "test-client.apps.googleusercontent.com")]
    [InlineData(true, "")]
    public async Task Off_OrWithoutAClientId_IsOk_WhateverElseIsMissing(bool enabled, string clientId)
    {
        var posture = await Contributor(new GoogleAuthSettings { Enabled = enabled, ClientId = clientId }).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok, "Google is not offered, so nobody is refused after Google");
    }
}
