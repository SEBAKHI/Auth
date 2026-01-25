using Auth_API.Modules.OrganizationManagement.Commands;
using Auth_API.Tests.Helpers;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for EnableApplicationCommandHandler.
/// </summary>
public class EnableApplicationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILogger<EnableApplicationCommandHandler>> _loggerMock;
    private readonly EnableApplicationCommandHandler _handler;

    public EnableApplicationCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<EnableApplicationCommandHandler>>();

        _handler = new EnableApplicationCommandHandler(
            _organizationRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidData_EnablesApplicationSuccessfully()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var enabledById = Guid.NewGuid();

        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: appId,
            SubscriptionTier: "pro",
            ExpiresAt: null)
        { EnabledBy = enabledById };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var application = TestHelpers.CreateApplication(
            id: appId,
            code: "DATA-TRANSFER",
            name: "Data Transfer",
            description: "Transfer data between systems");
        var enabledByUser = TestHelpers.CreateUser(id: enabledById, firstName: "Admin", lastName: "User");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _organizationRepositoryMock
            .Setup(r => r.GetApplicationSubscriptionAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationApplication?)null);

        _organizationRepositoryMock
            .Setup(r => r.EnableApplicationAsync(It.IsAny<OrganizationApplication>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationApplication sub, CancellationToken _) => sub);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(enabledById, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabledByUser);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.OrganizationId.Should().Be(orgId);
        result.Value.ApplicationId.Should().Be(appId);
        result.Value.SubscriptionTier.Should().Be("pro");
        result.Value.IsActive.Should().BeTrue();
        result.Value.EnabledBy.Should().Be(enabledById);
        result.Value.EnabledByName.Should().Be("Admin User");

        _organizationRepositoryMock.Verify(
            r => r.EnableApplicationAsync(It.Is<OrganizationApplication>(s =>
                s.OrganizationId == orgId &&
                s.ApplicationId == appId &&
                s.EnabledBy == enabledById &&
                s.SubscriptionTier == "pro"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: Guid.NewGuid(),
            SubscriptionTier: "basic",
            ExpiresAt: null)
        { EnabledBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenOrganizationInactive_ReturnsForbiddenError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: Guid.NewGuid(),
            SubscriptionTier: "basic",
            ExpiresAt: null)
        { EnabledBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Inactive Org", isActive: false);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        result.FirstError.Code.Should().Be("Organization.Inactive");
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: appId,
            SubscriptionTier: "basic",
            ExpiresAt: null)
        { EnabledBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Application?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenApplicationAlreadyEnabled_ReturnsConflictError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: appId,
            SubscriptionTier: "basic",
            ExpiresAt: null)
        { EnabledBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var application = TestHelpers.CreateApplication(id: appId, code: "TEST-APP", name: "Test App");
        var existingSubscription = TestHelpers.CreateOrganizationApplication(
            organizationId: orgId,
            applicationId: appId,
            isActive: true);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _organizationRepositoryMock
            .Setup(r => r.GetApplicationSubscriptionAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSubscription);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WhenReactivatingInactiveSubscription_UpdatesExistingSubscription()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var enabledById = Guid.NewGuid();

        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: appId,
            SubscriptionTier: "enterprise",
            ExpiresAt: null)
        { EnabledBy = enabledById };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var application = TestHelpers.CreateApplication(id: appId, code: "TEST-APP", name: "Test App");
        var existingSubscription = TestHelpers.CreateOrganizationApplication(
            organizationId: orgId,
            applicationId: appId,
            isActive: false, // Inactive subscription
            subscriptionTier: "basic");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _organizationRepositoryMock
            .Setup(r => r.GetApplicationSubscriptionAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSubscription);

        _organizationRepositoryMock
            .Setup(r => r.UpdateApplicationSubscriptionAsync(It.IsAny<OrganizationApplication>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(enabledById, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        // Should update existing subscription, not create new
        _organizationRepositoryMock.Verify(
            r => r.UpdateApplicationSubscriptionAsync(It.IsAny<OrganizationApplication>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _organizationRepositoryMock.Verify(
            r => r.EnableApplicationAsync(It.IsAny<OrganizationApplication>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithExpirationDate_SetsExpiresAt()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var enabledById = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddYears(1);

        var command = new EnableApplicationCommand(
            OrganizationId: orgId,
            ApplicationId: appId,
            SubscriptionTier: "trial",
            ExpiresAt: expiresAt)
        { EnabledBy = enabledById };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var application = TestHelpers.CreateApplication(id: appId, code: "TEST-APP", name: "Test App");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _organizationRepositoryMock
            .Setup(r => r.GetApplicationSubscriptionAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationApplication?)null);

        OrganizationApplication? capturedSubscription = null;
        _organizationRepositoryMock
            .Setup(r => r.EnableApplicationAsync(It.IsAny<OrganizationApplication>(), It.IsAny<CancellationToken>()))
            .Callback<OrganizationApplication, CancellationToken>((s, _) => capturedSubscription = s)
            .ReturnsAsync((OrganizationApplication sub, CancellationToken _) => sub);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(enabledById, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.ExpiresAt.Should().Be(expiresAt);
        capturedSubscription!.ExpiresAt.Should().Be(expiresAt);
    }
}
