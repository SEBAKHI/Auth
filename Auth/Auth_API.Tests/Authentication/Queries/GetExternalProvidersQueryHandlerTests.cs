using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// Unit tests for GetExternalProvidersQueryHandler.
/// </summary>
public class GetExternalProvidersQueryHandlerTests
{
    private readonly Mock<IExternalAuthProviderRepository> _providerRepositoryMock;
    private readonly GetExternalProvidersQueryHandler _handler;

    public GetExternalProvidersQueryHandlerTests()
    {
        _providerRepositoryMock = new Mock<IExternalAuthProviderRepository>();

        _handler = new GetExternalProvidersQueryHandler(
            _providerRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_NoProviders_ReturnsEmptyList()
    {
        // Arrange
        var query = new GetExternalProvidersQuery();
        var providers = new List<Auth.Domain.Entities.ExternalAuthProvider>();

        _providerRepositoryMock
            .Setup(r => r.GetAllEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithProviders_ReturnsMappedProviderResponses()
    {
        // Arrange
        var query = new GetExternalProvidersQuery();
        var providers = new List<Auth.Domain.Entities.ExternalAuthProvider>
        {
            TestHelpers.CreateExternalAuthProvider(
                code: "google",
                name: "Google",
                iconUrl: "https://google.com/icon.png"),
            TestHelpers.CreateExternalAuthProvider(
                code: "microsoft",
                name: "Microsoft",
                iconUrl: "https://microsoft.com/icon.png"),
            TestHelpers.CreateExternalAuthProvider(
                code: "github",
                name: "GitHub",
                iconUrl: null)
        };

        _providerRepositoryMock
            .Setup(r => r.GetAllEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(3);

        result.Value[0].Code.Should().Be("google");
        result.Value[0].Name.Should().Be("Google");
        result.Value[0].IconUrl.Should().Be("https://google.com/icon.png");

        result.Value[1].Code.Should().Be("microsoft");
        result.Value[1].Name.Should().Be("Microsoft");

        result.Value[2].Code.Should().Be("github");
        result.Value[2].Name.Should().Be("GitHub");
        result.Value[2].IconUrl.Should().BeNull();
    }
}
