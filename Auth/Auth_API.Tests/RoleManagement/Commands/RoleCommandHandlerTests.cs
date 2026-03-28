using Auth.Application.Features.Roles.CreateRole;
using Auth.Application.Features.Roles.UpdateRole;
using Auth.Application.Features.Roles.DeleteRole;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.RoleManagement.Commands;

/// <summary>
/// Unit tests for CreateRoleCommandHandler.
/// </summary>
public class CreateRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<CreateRoleCommandHandler>> _loggerMock;
    private readonly CreateRoleCommandHandler _handler;

    public CreateRoleCommandHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<CreateRoleCommandHandler>>();

        _handler = new CreateRoleCommandHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesRoleAndReturnsDto()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var command = new CreateRoleCommand(
            ApplicationId: applicationId,
            Code: "ADMIN",
            Name: "Administrator",
            Description: "Full access role")
        { CreatedBy = createdBy };

        _roleRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(applicationId, "ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role role, CancellationToken _) => role);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Code.Should().Be("ADMIN");
        result.Value.Name.Should().Be("Administrator");
        result.Value.Description.Should().Be("Full access role");
        result.Value.ApplicationId.Should().Be(applicationId);

        _roleRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var command = new CreateRoleCommand(
            ApplicationId: applicationId,
            Code: "ADMIN",
            Name: "Administrator")
        { CreatedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(applicationId, "ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Role.DuplicateCode");

        _roleRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithPermissionIds_AssignsPermissions()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var permissionId1 = Guid.NewGuid();
        var permissionId2 = Guid.NewGuid();

        var permission1 = TestHelpers.CreatePermission(
            id: permissionId1,
            code: "users:read",
            name: "Read Users");

        var permission2 = TestHelpers.CreatePermission(
            id: permissionId2,
            code: "users:write",
            name: "Write Users");

        var command = new CreateRoleCommand(
            ApplicationId: applicationId,
            Code: "EDITOR",
            Name: "Editor",
            PermissionIds: new List<Guid> { permissionId1, permissionId2 })
        { CreatedBy = createdBy };

        _roleRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(applicationId, "EDITOR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role role, CancellationToken _) => role);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission1);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission2);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Permissions.Should().HaveCount(2);
        result.Value.Permissions.Should().Contain("users:read");
        result.Value.Permissions.Should().Contain("users:write");

        _permissionRepositoryMock.Verify(
            r => r.GrantToRoleAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}

/// <summary>
/// Unit tests for UpdateRoleCommandHandler.
/// </summary>
public class UpdateRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<UpdateRoleCommandHandler>> _loggerMock;
    private readonly UpdateRoleCommandHandler _handler;

    public UpdateRoleCommandHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<UpdateRoleCommandHandler>>();

        _handler = new UpdateRoleCommandHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesRoleAndReturnsDto()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var modifiedBy = Guid.NewGuid();
        var role = TestHelpers.CreateRole(
            id: roleId,
            code: "EDITOR",
            name: "Editor",
            description: "Original description");

        var permissions = new List<Permission>
        {
            TestHelpers.CreatePermission(code: "users:read", name: "Read Users")
        };

        var command = new UpdateRoleCommand(
            Id: roleId,
            Name: "Senior Editor",
            Description: "Updated description")
        { ModifiedBy = modifiedBy };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(roleId);
        result.Value.Name.Should().Be("Senior Editor");

        _roleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var command = new UpdateRoleCommand(
            Id: roleId,
            Name: "Updated Name")
        { ModifiedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Role.NotFound");

        _roleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbiddenError()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var systemRole = TestHelpers.CreateRole(
            id: roleId,
            code: "SUPER-ADMIN",
            name: "Super Admin",
            isSystem: true);

        var command = new UpdateRoleCommand(
            Id: roleId,
            Name: "Renamed Admin")
        { ModifiedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemRole);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        result.FirstError.Code.Should().Be("Role.CannotUpdateSystemRole");

        _roleRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for DeleteRoleCommandHandler.
/// </summary>
public class DeleteRoleCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<ILogger<DeleteRoleCommandHandler>> _loggerMock;
    private readonly DeleteRoleCommandHandler _handler;

    public DeleteRoleCommandHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _loggerMock = new Mock<ILogger<DeleteRoleCommandHandler>>();

        _handler = new DeleteRoleCommandHandler(
            _roleRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRole_DeletesSuccessfully()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(
            id: roleId,
            code: "TEMP-ROLE",
            name: "Temporary Role");

        var command = new DeleteRoleCommand(Id: roleId)
        { DeletedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _roleRepositoryMock.Verify(
            r => r.DeleteAsync(roleId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var command = new DeleteRoleCommand(Id: roleId)
        { DeletedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Role.NotFound");

        _roleRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SystemRole_ReturnsForbiddenError()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var systemRole = TestHelpers.CreateRole(
            id: roleId,
            code: "SYSTEM-ADMIN",
            name: "System Admin",
            isSystem: true);

        var command = new DeleteRoleCommand(Id: roleId)
        { DeletedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemRole);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        result.FirstError.Code.Should().Be("Role.CannotDeleteSystemRole");

        _roleRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
