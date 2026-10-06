using MediatR;
using Auth.Application.Common;
using Auth.Application.Features.Roles.CreateRole;
using Auth.Application.Features.Roles.UpdateRole;
using Auth.Application.Features.Roles.DeleteRole;
using Auth.Application.Features.Roles.GrantRolePermission;
using Auth.Application.Features.Roles.RevokeRolePermission;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
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

        // The actor holds "*" unless a test says otherwise: these cases cover
        // role creation, not the no-amplification rule, which has its own suite.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["*"]);

        _handler = new CreateRoleCommandHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            new Mock<IPublisher>().Object,
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
            Code: "Admin",
            Name: "Administrator",
            Description: "Full access role")
        { CreatedBy = createdBy };

        _roleRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(applicationId, "Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _roleRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role role, CancellationToken _) => role);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        // Stored lowercase, like every seeded role, whatever case was sent (OI-73).
        result.Value.Code.Should().Be("admin");
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

        // The role's own application: a role holds no other scope's permissions.
        var permission1 = TestHelpers.CreatePermission(
            id: permissionId1,
            applicationId: applicationId,
            code: "users:read",
            name: "Read Users");

        var permission2 = TestHelpers.CreatePermission(
            id: permissionId2,
            applicationId: applicationId,
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

    [Fact]
    public async Task Handle_WithAPermissionOfAnotherScope_ReturnsPermissionNotForApplicationAndWritesNothing()
    {
        // Arrange: two of the role's own application's permissions around one
        // platform permission. The path is API-only (the console sends none).
        var applicationId = Guid.NewGuid();
        var ownFirst = TestHelpers.CreatePermission(applicationId: applicationId, code: "edis:fairs:view");
        var platform = TestHelpers.CreatePermission(applicationId: null, code: "users:read");
        var ownLast = TestHelpers.CreatePermission(applicationId: applicationId, code: "edis:fairs:manage");

        var command = new CreateRoleCommand(
            ApplicationId: applicationId,
            Code: "institution_manager",
            Name: "Institution manager",
            PermissionIds: new List<Guid> { ownFirst.Id, platform.Id, ownLast.Id })
        { CreatedBy = Guid.NewGuid() };

        _roleRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        foreach (var permission in new[] { ownFirst, platform, ownLast })
        {
            _permissionRepositoryMock
                .Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(permission);
        }

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: refused before the role is written, so nothing is left behind,
        // and before the no-amplification guard reads what the actor holds.
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(RoleErrors.PermissionNotForApplication.Code);
        result.FirstError.Type.Should().Be(ErrorType.Validation);

        _roleRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _permissionRepositoryMock.Verify(
            r => r.GrantToRoleAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
            new Mock<IPublisher>().Object,
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
            new Mock<IPublisher>().Object,
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

/// <summary>
/// Unit tests for GrantRolePermissionCommandHandler: a role holds only its own
/// application's permissions, and a platform role only platform ones (OI-74).
/// </summary>
public class GrantRolePermissionCommandHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly GrantRolePermissionCommandHandler _handler;

    public GrantRolePermissionCommandHandlerTests()
    {
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission>());

        _handler = new GrantRolePermissionCommandHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            _publisherMock.Object,
            new Mock<ILogger<GrantRolePermissionCommandHandler>>().Object);
    }

    private GrantRolePermissionCommand Arrange(Role role, Permission permission, params string[] actorHolds)
    {
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(actorHolds);

        return new GrantRolePermissionCommand(role.Id, permission.Id) { GrantedBy = Guid.NewGuid() };
    }

    private static readonly Guid Edis = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Crm = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static TheoryData<Guid?, Guid?, string> MismatchedScopes() => new()
    {
        // (a) An application role and a platform permission, the wildcard included.
        { Edis, null, "*" },
        { Edis, null, "users:read" },
        // (b) An application role and another application's permission.
        { Edis, Crm, "crm:leads:read" },
        // (c) A platform role and an application's permission.
        { null, Edis, "edis:fairs:view" },
    };

    [Theory]
    [MemberData(nameof(MismatchedScopes))]
    public async Task Handle_PermissionOfAnotherScope_ReturnsPermissionNotForApplication(
        Guid? roleApplicationId, Guid? permissionApplicationId, string permissionCode)
    {
        // Arrange: the actor holds nothing, so a guard that ran first would
        // answer with its own error. The scope answer must not depend on it.
        var role = TestHelpers.CreateRole(applicationId: roleApplicationId);
        var permission = TestHelpers.CreatePermission(
            applicationId: permissionApplicationId, code: permissionCode);
        var command = Arrange(role, permission);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(RoleErrors.PermissionNotForApplication.Code);
        result.FirstError.Type.Should().Be(ErrorType.Validation);

        _permissionRepositoryMock.Verify(
            r => r.GrantToRoleAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _publisherMock.Invocations.Should().BeEmpty();
    }

    public static TheoryData<Guid?, string> MatchingScopes() => new()
    {
        // (d) The role's own application.
        { Edis, "edis:fairs:view" },
        // (e) Both platform, the wildcard included.
        { null, "*" },
        { null, "users:read" },
    };

    [Theory]
    [MemberData(nameof(MatchingScopes))]
    public async Task Handle_PermissionOfTheRolesScope_GrantsAndPublishes(
        Guid? applicationId, string permissionCode)
    {
        // Arrange
        var role = TestHelpers.CreateRole(applicationId: applicationId);
        var permission = TestHelpers.CreatePermission(applicationId: applicationId, code: permissionCode);
        var command = Arrange(role, permission, "*");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _permissionRepositoryMock.Verify(
            r => r.GrantToRoleAsync(
                It.Is<RolePermission>(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<RolePermissionGrantedEvent>(e => e.RoleId == role.Id && e.PermissionId == permission.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

/// <summary>
/// Unit tests for RevokeRolePermissionCommandHandler. Removal takes no scope
/// check: a role-permission row written before the rule existed (OI-74) must
/// stay removable.
/// </summary>
public class RevokeRolePermissionCommandHandlerTests
{
    [Fact]
    public async Task Handle_GrantedPermissionOfAnotherScope_RevokesIt()
    {
        // Arrange: an application role still holding the platform wildcard.
        var roleRepositoryMock = new Mock<IRoleRepository>();
        var permissionRepositoryMock = new Mock<IPermissionRepository>();
        var role = TestHelpers.CreateRole(applicationId: Guid.NewGuid());
        var mismatched = TestHelpers.CreatePermission(applicationId: null, code: "*", level: 0, isWildcard: true);

        roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { mismatched });

        var handler = new RevokeRolePermissionCommandHandler(
            roleRepositoryMock.Object,
            permissionRepositoryMock.Object,
            new Mock<IPublisher>().Object,
            new Mock<ILogger<RevokeRolePermissionCommandHandler>>().Object);

        // Act
        var result = await handler.Handle(
            new RevokeRolePermissionCommand(role.Id, mismatched.Id) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        permissionRepositoryMock.Verify(
            r => r.RevokeFromRoleAsync(role.Id, mismatched.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
