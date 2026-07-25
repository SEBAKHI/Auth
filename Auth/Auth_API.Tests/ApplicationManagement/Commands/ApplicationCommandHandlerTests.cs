using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Features.Applications.DeleteApplication;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Commands;

/// <summary>
/// Unit tests for CreateApplicationCommandHandler.
/// </summary>
public class CreateApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<CreateApplicationCommandHandler>> _loggerMock;
    private readonly CreateApplicationCommandHandler _handler;

    public CreateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<CreateApplicationCommandHandler>>();

        _handler = new CreateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesAndReturnsDto()
    {
        // Arrange
        var createdBy = Guid.NewGuid();
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM Application",
            Description: "Customer Relationship Management",
            BaseUrl: "https://crm.example.com")
        { CreatedBy = createdBy };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Code.Should().Be("CRM");
        result.Value.Name.Should().Be("CRM Application");
        result.Value.Description.Should().Be("Customer Relationship Management");
        result.Value.BaseUrl.Should().Be("https://crm.example.com");
        result.Value.IsActive.Should().BeTrue();

        _applicationRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<ApplicationEntity>(a =>
                a.Code == "CRM" &&
                a.Name == "CRM Application"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var command = new CreateApplicationCommand(
            Code: "AUTH",
            Name: "Duplicate App")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.DuplicateCode");

        _applicationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for UpdateApplicationCommandHandler.
/// </summary>
public class UpdateApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<UpdateApplicationCommandHandler>> _loggerMock;
    private readonly UpdateApplicationCommandHandler _handler;

    public UpdateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<UpdateApplicationCommandHandler>>();

        _handler = new UpdateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesAndReturnsDto()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var modifiedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "Old Name");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "Updated CRM",
            Description: "Updated description",
            BaseUrl: "https://crm-v2.example.com",
            LogoUrl: "https://logo.example.com/crm.png",
            ContactEmail: "crm@example.com",
            AllowSelfRegistration: true,
            RequireTwoFactor: true,
            RequireEmailVerification: true,
            SessionTimeoutMinutes: 120,
            MaxConcurrentSessions: 10)
        { ModifiedBy = modifiedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(appId);
        result.Value.Code.Should().Be("CRM");
        result.Value.Name.Should().Be("Updated CRM");
        result.Value.Description.Should().Be("Updated description");
        result.Value.BaseUrl.Should().Be("https://crm-v2.example.com");
        result.Value.LogoUrl.Should().Be("https://logo.example.com/crm.png");
        result.Value.ContactEmail.Should().Be("crm@example.com");
        result.Value.AllowSelfRegistration.Should().BeTrue();
        result.Value.RequireTwoFactor.Should().BeTrue();
        result.Value.RequireEmailVerification.Should().BeTrue();
        result.Value.SessionTimeoutMinutes.Should().Be(120);
        result.Value.MaxConcurrentSessions.Should().Be(10);

        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "Updated Name")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for DeleteApplicationCommandHandler.
/// </summary>
public class DeleteApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<DeleteApplicationCommandHandler>> _loggerMock;
    private readonly DeleteApplicationCommandHandler _handler;

    public DeleteApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<DeleteApplicationCommandHandler>>();

        _handler = new DeleteApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidApplication_SoftDeletesWithActingUser()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var deletedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");

        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = deletedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeTrue();

        // The acting user (not the application id) must be recorded as DeletedBy.
        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AnyCodeIncludingAuth_DeletesSuccessfully()
    {
        // Regression for the retired "system application" guard: deletion is
        // decided by Id and dependency state only — never by name or code.
        // Uses the exact lowercase code the old case-sensitive guard missed.
        var appId = Guid.NewGuid();
        var deletedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "auth", name: "Auth System");

        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = deletedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveUserAssignments_ReturnsConflict()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.HasActiveUsers");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveOrganizations_ReturnsConflict()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.HasActiveOrganizations");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
