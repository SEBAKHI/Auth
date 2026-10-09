using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using ErrorOr;

namespace Auth_API.Tests.ApplicationManagement.Queries;

/// <summary>
/// Unit tests for GetPublicBrandingQueryHandler.
/// </summary>
public class GetPublicBrandingQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetPublicBrandingQueryHandler _handler;

    public GetPublicBrandingQueryHandlerTests()
    {
        _handler = new GetPublicBrandingQueryHandler(
            _applicationRepositoryMock.Object,
            ApplicationTestImages.Composer());
    }

    [Fact]
    public async Task Handle_UnknownClient_ReturnsNotFound()
    {
        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("NOPE"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_InactiveApplication_ReturnsNotFound()
    {
        // Arrange — inactive must be indistinguishable from unknown so the
        // anonymous endpoint cannot probe the catalog.
        var application = TestHelpers.CreateApplication(code: "CRM", isActive: false);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ActiveApplication_ReturnsNameAndLogoOnly()
    {
        // Arrange
        var application = TestHelpers.CreateApplication(
            code: "CRM",
            name: "Acme CRM",
            logoUrl: "https://auth.example.com/uploads/images/crm.png");
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("Acme CRM");
        result.Value.LogoUrl.Should().Be("https://auth.example.com/uploads/images/crm.png");
    }

    [Fact]
    public async Task Handle_ActiveApplicationWithUploadedLogoKey_ReturnsComposedAbsoluteUrl()
    {
        // Arrange — an uploaded logo is stored as a bare storage key. The sign-in
        // pages render on another origin, so a raw key resolves against the wrong
        // host and the image breaks.
        var application = TestHelpers.CreateApplication(
            code: "CRM",
            name: "Acme CRM",
            logoUrl: "c60d12817fe544c68b6ebb311ad661b7.webp");
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.LogoUrl.Should().Be(
            $"{ApplicationTestImages.PublicBaseUrl}/c60d12817fe544c68b6ebb311ad661b7.webp");
    }

    [Fact]
    public async Task Handle_ActiveApplicationWithoutLogo_ReturnsNullLogo()
    {
        // Arrange
        var application = TestHelpers.CreateApplication(code: "CRM", name: "Acme CRM", logoUrl: null);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.LogoUrl.Should().BeNull();
        result.Value.LogoUrlDark.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ActiveApplicationWithADarkLogo_ReturnsBothComposed()
    {
        // The sign-in pages pick the logo by the visitor's theme, so both
        // variants travel; the dark one is composed like the light one.
        var application = TestHelpers.CreateApplication(
            code: "CRM", name: "Acme CRM", logoUrl: "light.webp", logoUrlDark: "dark.webp");
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/light.webp");
        result.Value.LogoUrlDark.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/dark.webp");
    }
}
