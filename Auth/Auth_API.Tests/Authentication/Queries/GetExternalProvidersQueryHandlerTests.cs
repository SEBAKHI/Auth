using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// Unit tests for GetExternalProvidersQueryHandler.
/// <para>
/// The endpoint is what the sign-in pages render their provider buttons from, so
/// it must list a provider only when a sign-in through it could actually succeed:
/// directory row enabled AND configuration enabled AND a public client id
/// present. It also carries that client id, so the SPA mints tokens with the same
/// value the API validates instead of a build-time copy that can drift.
/// </para>
/// </summary>
public class GetExternalProvidersQueryHandlerTests
{
    private const string GoogleClientId = "google-client-id.apps.googleusercontent.com";
    private const string AppleServicesId = "com.example.web";

    private readonly Mock<IExternalAuthProviderRepository> _providerRepositoryMock = new();

    private static GetExternalProvidersQueryHandler CreateHandler(
        Mock<IExternalAuthProviderRepository> repository,
        ExternalAuthSettings settings)
    {
        var options = new Mock<IOptionsSnapshot<ExternalAuthSettings>>();
        options.SetupGet(o => o.Value).Returns(settings);
        return new GetExternalProvidersQueryHandler(repository.Object, options.Object);
    }

    private static ExternalAuthSettings FullyConfigured() => new()
    {
        Google = new GoogleAuthSettings { Enabled = true, ClientId = GoogleClientId },
        Apple = new AppleAuthSettings { Enabled = true, ServicesId = AppleServicesId }
    };

    private void HasProviders(params Auth.Domain.Entities.ExternalAuthProvider[] providers)
        => _providerRepositoryMock
            .Setup(r => r.GetAllEnabledAsync(
                It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers.ToList());

    [Fact]
    public async Task Handle_NoProviders_ReturnsEmptyList()
    {
        HasProviders();

        var result = await CreateHandler(_providerRepositoryMock, FullyConfigured())
            .Handle(new GetExternalProvidersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConfiguredProviders_ReturnsThemWithTheirPublicClientId()
    {
        HasProviders(
            TestHelpers.CreateExternalAuthProvider(
                code: "google", name: "Google", iconUrl: "https://google.com/icon.png"),
            TestHelpers.CreateExternalAuthProvider(
                code: "apple", name: "Apple", iconUrl: null));

        var result = await CreateHandler(_providerRepositoryMock, FullyConfigured())
            .Handle(new GetExternalProvidersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);

        result.Value[0].Code.Should().Be("google");
        result.Value[0].Name.Should().Be("Google");
        result.Value[0].IconUrl.Should().Be("https://google.com/icon.png");
        result.Value[0].ClientId.Should().Be(
            GoogleClientId,
            "the SPA mints the token with this value and the API validates the audience against it");

        result.Value[1].Code.Should().Be("apple");
        result.Value[1].IconUrl.Should().BeNull();
        result.Value[1].ClientId.Should().Be(AppleServicesId, "Apple's Services ID is its client id");
    }

    [Fact]
    public async Task Handle_ProviderDisabledInConfiguration_IsNotOffered()
    {
        HasProviders(TestHelpers.CreateExternalAuthProvider(code: "google", name: "Google"));

        var settings = FullyConfigured();
        settings.Google!.Enabled = false;

        var result = await CreateHandler(_providerRepositoryMock, settings)
            .Handle(new GetExternalProvidersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty(
            "the console's per-provider toggle must govern the sign-in button, not only server-side validation");
    }

    [Fact]
    public async Task Handle_ProviderWithoutClientId_IsNotOffered()
    {
        HasProviders(TestHelpers.CreateExternalAuthProvider(code: "google", name: "Google"));

        var settings = FullyConfigured();
        settings.Google!.ClientId = string.Empty;

        var result = await CreateHandler(_providerRepositoryMock, settings)
            .Handle(new GetExternalProvidersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty(
            "without a client id the button could only ever produce a token the API rejects");
    }

    [Fact]
    public async Task Handle_ProviderWithNoConfigurationSection_IsNotOffered()
    {
        // A directory row for a provider the API has no configuration for at all
        // (an unimplemented code, or a section left unset).
        HasProviders(
            TestHelpers.CreateExternalAuthProvider(code: "github", name: "GitHub"),
            TestHelpers.CreateExternalAuthProvider(code: "google", name: "Google"));

        var result = await CreateHandler(_providerRepositoryMock, new ExternalAuthSettings())
            .Handle(new GetExternalProvidersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }
}
