using Auth.Application.Features.Users.AssignRole;
using Auth.Application.Features.Users.RemoveUserRole;
using Auth.Application.Features.Users.GrantUserPermission;
using Auth.Application.Features.Users.RevokeUserPermission;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class AssignRoleCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly AssignRoleCommandHandler _handler;

    public AssignRoleCommandHandlerTests()
    {
        _handler = new AssignRoleCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<AssignRoleCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidAssignment_AssignsRoleAndPublishesEvent()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var role = TestHelpers.CreateRole(id: roleId, name: "Admin");
        var command = new AssignRoleCommand(userId, roleId) { AssignedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _roleRepositoryMock.Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Role>());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _roleRepositoryMock.Verify(r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<RoleAssignedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(
            new AssignRoleCommand(Guid.NewGuid(), Guid.NewGuid()) { AssignedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Role?)null);

        var result = await _handler.Handle(
            new AssignRoleCommand(userId, Guid.NewGuid()) { AssignedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AlreadyAssigned_ReturnsConflictError()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(id: roleId);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        // Scoped by (role, application): the existing assignment is the
        // platform-wide one, matching the platform-wide request below.
        _userRepositoryMock
            .Setup(r => r.GetUserRoleAsync(userId, roleId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserRole.Create(userId, roleId, Guid.NewGuid()));

        var result = await _handler.Handle(
            new AssignRoleCommand(userId, roleId) { AssignedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
    }
}

public class RemoveUserRoleCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly RemoveUserRoleCommandHandler _handler;

    public RemoveUserRoleCommandHandlerTests()
    {
        _handler = new RemoveUserRoleCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            new Mock<ILogger<RemoveUserRoleCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidRemoval_RemovesRole()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new RemoveUserRoleCommand(userId, roleId) { RemovedBy = Guid.NewGuid() };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole(id: roleId));
        _userRepositoryMock.Setup(r => r.HasRoleAsync(userId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RemoveRoleAsync(userId, roleId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_RoleNotAssigned_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole(id: roleId));
        _userRepositoryMock.Setup(r => r.HasRoleAsync(userId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _handler.Handle(
            new RemoveUserRoleCommand(userId, roleId) { RemovedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class GrantUserPermissionCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly GrantUserPermissionCommandHandler _handler;

    public GrantUserPermissionCommandHandlerTests()
    {
        _handler = new GrantUserPermissionCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new Mock<ILogger<GrantUserPermissionCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidGrant_GrantsPermission()
    {
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var permission = TestHelpers.CreatePermission(id: permissionId);
        var command = new GrantUserPermissionCommand(userId, permissionId) { GrantedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        _userRepositoryMock.Setup(r => r.GetUserPermissionsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserPermission>());
        _userRepositoryMock.Setup(r => r.GrantPermissionAsync(It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserPermission(userId: userId, permissionId: permissionId));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(
            new GrantUserPermissionCommand(Guid.NewGuid(), Guid.NewGuid()) { GrantedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class RevokeUserPermissionCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly RevokeUserPermissionCommandHandler _handler;

    public RevokeUserPermissionCommandHandlerTests()
    {
        _handler = new RevokeUserPermissionCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new Mock<ILogger<RevokeUserPermissionCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidRevocation_RevokesPermission()
    {
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var permission = TestHelpers.CreatePermission(id: permissionId);

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        _userRepositoryMock.Setup(r => r.HasDirectPermissionAsync(userId, permissionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(userId, permissionId) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RevokePermissionAsync(userId, permissionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(Guid.NewGuid(), Guid.NewGuid()) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
