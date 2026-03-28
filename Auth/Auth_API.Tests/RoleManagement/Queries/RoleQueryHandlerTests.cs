using Auth.Application.Features.Roles.GetRoleById;
using Auth.Application.Features.Roles.GetRoles;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.RoleManagement.Queries;

/// <summary>
/// Unit tests for GetRoleByIdQueryHandler.
/// </summary>
public class GetRoleByIdQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly GetRoleByIdQueryHandler _handler;

    public GetRoleByIdQueryHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();

        _handler = new GetRoleByIdQueryHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsRoleDto()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(
            id: roleId,
            applicationId: applicationId,
            code: "EDITOR",
            name: "Editor",
            description: "Can edit content");

        var permissions = new List<Permission>
        {
            TestHelpers.CreatePermission(code: "content:read", name: "Read Content"),
            TestHelpers.CreatePermission(code: "content:write", name: "Write Content")
        };

        var query = new GetRoleByIdQuery(Id: roleId);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(roleId);
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.Code.Should().Be("EDITOR");
        result.Value.Name.Should().Be("Editor");
        result.Value.Description.Should().Be("Can edit content");
        result.Value.Permissions.Should().HaveCount(2);
        result.Value.Permissions.Should().Contain("content:read");
        result.Value.Permissions.Should().Contain("content:write");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var query = new GetRoleByIdQuery(Id: roleId);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Role.NotFound");
    }
}

/// <summary>
/// Unit tests for GetRolesQueryHandler.
/// </summary>
public class GetRolesQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly GetRolesQueryHandler _handler;

    public GetRolesQueryHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();

        _handler = new GetRolesQueryHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithApplicationId_ReturnsFilteredRoles()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var role1 = TestHelpers.CreateRole(
            applicationId: applicationId,
            code: "ADMIN",
            name: "Admin");
        var role2 = TestHelpers.CreateRole(
            applicationId: applicationId,
            code: "VIEWER",
            name: "Viewer");

        var roles = new List<Role> { role1, role2 };

        var query = new GetRolesQuery(ApplicationId: applicationId);

        _roleRepositoryMock
            .Setup(r => r.GetByApplicationAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value[0].Code.Should().Be("ADMIN");
        result.Value[1].Code.Should().Be("VIEWER");

        _roleRepositoryMock.Verify(
            r => r.GetByApplicationAsync(applicationId, It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoApplicationId_ReturnsAllRoles()
    {
        // Arrange
        var role1 = TestHelpers.CreateRole(code: "GLOBAL-ADMIN", name: "Global Admin");
        var role2 = TestHelpers.CreateRole(code: "GLOBAL-VIEWER", name: "Global Viewer");
        var role3 = TestHelpers.CreateRole(code: "GLOBAL-EDITOR", name: "Global Editor");

        var roles = new List<Role> { role1, role2, role3 };

        var query = new GetRolesQuery();

        _roleRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(3);

        _roleRepositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetByApplicationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
