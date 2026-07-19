using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Domain.Interfaces.Repositories;
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
        _handler = new GetPublicBrandingQueryHandler(_applicationRepositoryMock.Object);
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
            name: "Astoom CRM",
            logoUrl: "https://auth.example.com/uploads/images/crm.png");
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("Astoom CRM");
        result.Value.LogoUrl.Should().Be("https://auth.example.com/uploads/images/crm.png");
    }
}
