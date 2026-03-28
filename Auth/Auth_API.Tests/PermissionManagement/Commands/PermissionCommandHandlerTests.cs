using Auth.Application.Features.Permissions.CreatePermission;
using Auth.Application.Features.Permissions.UpdatePermission;
using Auth.Application.Features.Permissions.DeletePermission;
using Auth.Application.Features.Permissions.AddPermissionImplication;
using Auth.Application.Features.Permissions.RemovePermissionImplication;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PermissionManagement.Commands;

/// <summary>
/// Unit tests for CreatePermissionCommandHandler.
/// </summary>
public class CreatePermissionCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<CreatePermissionCommandHandler>> _loggerMock;
    private readonly CreatePermissionCommandHandler _handler;

    public CreatePermissionCommandHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<CreatePermissionCommandHandler>>();

        _handler = new CreatePermissionCommandHandler(
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesPermissionAndReturnsDto()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreatePermissionCommand(
            ApplicationId: applicationId,
            Code: "orders:read",
            Name: "Read Orders",
            Description: "Allows reading order data")
        { CreatedBy = userId };

        var application = TestHelpers.CreateApplication(id: applicationId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _permissionRepositoryMock
            .Setup(r => r.GetByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        _permissionRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission p, CancellationToken _) => p);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Code.Should().Be(command.Code.ToLowerInvariant());
        result.Value.Name.Should().Be(command.Name);
        result.Value.Description.Should().Be(command.Description);
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.IsActive.Should().BeTrue();

        _permissionRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var command = new CreatePermissionCommand(
            ApplicationId: applicationId,
            Code: "orders:read",
            Name: "Read Orders")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.Application?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _permissionRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var command = new CreatePermissionCommand(
            ApplicationId: applicationId,
            Code: "orders:read",
            Name: "Read Orders")
        { CreatedBy = Guid.NewGuid() };

        var application = TestHelpers.CreateApplication(id: applicationId);
        var existingPermission = TestHelpers.CreatePermission(
            applicationId: applicationId,
            code: "orders:read");

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _permissionRepositoryMock
            .Setup(r => r.GetByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPermission);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Permission.DuplicateCode");

        _permissionRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for UpdatePermissionCommandHandler.
/// </summary>
public class UpdatePermissionCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<UpdatePermissionCommandHandler>> _loggerMock;
    private readonly UpdatePermissionCommandHandler _handler;

    public UpdatePermissionCommandHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<UpdatePermissionCommandHandler>>();

        _handler = new UpdatePermissionCommandHandler(
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesAndReturnsDto()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new UpdatePermissionCommand(
            Id: permissionId,
            Name: "Updated Permission Name",
            Description: "Updated description")
        { ModifiedBy = userId };

        var permission = TestHelpers.CreatePermission(
            id: permissionId,
            code: "orders:read",
            name: "Original Name");

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);

        _permissionRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(permissionId);
        result.Value.Name.Should().Be(command.Name);
        result.Value.Description.Should().Be(command.Description);

        _permissionRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var command = new UpdatePermissionCommand(
            Id: permissionId,
            Name: "Updated Name")
        { ModifiedBy = Guid.NewGuid() };

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotFound");

        _permissionRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for DeletePermissionCommandHandler.
/// </summary>
public class DeletePermissionCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<DeletePermissionCommandHandler>> _loggerMock;
    private readonly DeletePermissionCommandHandler _handler;

    public DeletePermissionCommandHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<DeletePermissionCommandHandler>>();

        _handler = new DeletePermissionCommandHandler(
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidPermission_DeletesSuccessfully()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var command = new DeletePermissionCommand(Id: permissionId)
        { DeletedBy = Guid.NewGuid() };

        var permission = TestHelpers.CreatePermission(
            id: permissionId,
            code: "orders:delete");

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);

        _permissionRepositoryMock
            .Setup(r => r.DeleteAsync(permissionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeTrue();

        _permissionRepositoryMock.Verify(
            r => r.DeleteAsync(permissionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var command = new DeletePermissionCommand(Id: permissionId)
        { DeletedBy = Guid.NewGuid() };

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotFound");

        _permissionRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WildcardPermission_ReturnsForbidden()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var command = new DeletePermissionCommand(Id: permissionId)
        { DeletedBy = Guid.NewGuid() };

        var wildcardPermission = TestHelpers.CreatePermission(
            id: permissionId,
            code: "*",
            isWildcard: true);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wildcardPermission);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        result.FirstError.Code.Should().Be("Permission.CannotDeleteSystemPermission");

        _permissionRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for AddPermissionImplicationCommandHandler.
/// </summary>
public class AddPermissionImplicationCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<AddPermissionImplicationCommandHandler>> _loggerMock;
    private readonly AddPermissionImplicationCommandHandler _handler;

    public AddPermissionImplicationCommandHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<AddPermissionImplicationCommandHandler>>();

        _handler = new AddPermissionImplicationCommandHandler(
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidImplication_AddsSuccessfully()
    {
        // Arrange
        var sourcePermissionId = Guid.NewGuid();
        var impliedPermissionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new AddPermissionImplicationCommand(
            PermissionId: sourcePermissionId,
            ImpliedPermissionId: impliedPermissionId)
        { CreatedBy = userId };

        var sourcePermission = TestHelpers.CreatePermission(
            id: sourcePermissionId,
            code: "orders:manage");

        var impliedPermission = TestHelpers.CreatePermission(
            id: impliedPermissionId,
            code: "orders:read");

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(sourcePermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePermission);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impliedPermission);

        _permissionRepositoryMock
            .Setup(r => r.ImplicationExistsAsync(sourcePermissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _permissionRepositoryMock
            .Setup(r => r.WouldCreateCircularImplicationAsync(sourcePermissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _permissionRepositoryMock
            .Setup(r => r.AddImplicationAsync(It.IsAny<PermissionImplication>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionImplication pi, CancellationToken _) => pi);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeTrue();

        _permissionRepositoryMock.Verify(
            r => r.AddImplicationAsync(It.IsAny<PermissionImplication>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SourceNotFound_ReturnsError()
    {
        // Arrange
        var sourcePermissionId = Guid.NewGuid();
        var impliedPermissionId = Guid.NewGuid();
        var command = new AddPermissionImplicationCommand(
            PermissionId: sourcePermissionId,
            ImpliedPermissionId: impliedPermissionId)
        { CreatedBy = Guid.NewGuid() };

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(sourcePermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotFound");

        _permissionRepositoryMock.Verify(
            r => r.AddImplicationAsync(It.IsAny<PermissionImplication>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_CircularImplication_ReturnsError()
    {
        // Arrange
        var sourcePermissionId = Guid.NewGuid();
        var impliedPermissionId = Guid.NewGuid();
        var command = new AddPermissionImplicationCommand(
            PermissionId: sourcePermissionId,
            ImpliedPermissionId: impliedPermissionId)
        { CreatedBy = Guid.NewGuid() };

        var sourcePermission = TestHelpers.CreatePermission(
            id: sourcePermissionId,
            code: "orders:manage");

        var impliedPermission = TestHelpers.CreatePermission(
            id: impliedPermissionId,
            code: "orders:read");

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(sourcePermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePermission);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impliedPermission);

        _permissionRepositoryMock
            .Setup(r => r.ImplicationExistsAsync(sourcePermissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _permissionRepositoryMock
            .Setup(r => r.WouldCreateCircularImplicationAsync(sourcePermissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Code.Should().Be("Permission.CircularImplication");

        _permissionRepositoryMock.Verify(
            r => r.AddImplicationAsync(It.IsAny<PermissionImplication>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for RemovePermissionImplicationCommandHandler.
/// </summary>
public class RemovePermissionImplicationCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<RemovePermissionImplicationCommandHandler>> _loggerMock;
    private readonly RemovePermissionImplicationCommandHandler _handler;

    public RemovePermissionImplicationCommandHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<RemovePermissionImplicationCommandHandler>>();

        _handler = new RemovePermissionImplicationCommandHandler(
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRemoval_RemovesSuccessfully()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var impliedPermissionId = Guid.NewGuid();
        var command = new RemovePermissionImplicationCommand(
            PermissionId: permissionId,
            ImpliedPermissionId: impliedPermissionId)
        { RemovedBy = Guid.NewGuid() };

        _permissionRepositoryMock
            .Setup(r => r.ImplicationExistsAsync(permissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _permissionRepositoryMock
            .Setup(r => r.RemoveImplicationAsync(permissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeTrue();

        _permissionRepositoryMock.Verify(
            r => r.RemoveImplicationAsync(permissionId, impliedPermissionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ImplicationNotFound_ReturnsError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var impliedPermissionId = Guid.NewGuid();
        var command = new RemovePermissionImplicationCommand(
            PermissionId: permissionId,
            ImpliedPermissionId: impliedPermissionId)
        { RemovedBy = Guid.NewGuid() };

        _permissionRepositoryMock
            .Setup(r => r.ImplicationExistsAsync(permissionId, impliedPermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotGranted");

        _permissionRepositoryMock.Verify(
            r => r.RemoveImplicationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
