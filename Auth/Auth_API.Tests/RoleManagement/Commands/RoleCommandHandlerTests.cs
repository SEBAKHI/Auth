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
        var ownFirst = TestHelpers.CreatePermission(applicationId: applicationId, code: "shop:orders:view");
        var platform = TestHelpers.CreatePermission(applicationId: null, code: "users:read");
        var ownLast = TestHelpers.CreatePermission(applicationId: applicationId, code: "shop:orders:manage");

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
            TestHelpers.CreatePlatformGrantFactorGuard(),
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

    private static readonly Guid Shop = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Crm = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static TheoryData<Guid?, Guid?, string> MismatchedScopes() => new()
    {
        // (a) An application role and a platform permission, the wildcard included.
        { Shop, null, "*" },
        { Shop, null, "users:read" },
        // (b) An application role and another application's permission.
        { Shop, Crm, "crm:leads:read" },
        // (c) A platform role and an application's permission.
        { null, Shop, "shop:orders:view" },
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
        { Shop, "shop:orders:view" },
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

    [Fact]
    public async Task Handle_PermissionOfTheRolesScopeTheActorDoesNotHold_ReturnsCannotGrantHigher()
    {
        // Arrange: the scope matches, so the no-amplification guard still
        // decides; the scope check must not have replaced it.
        var role = TestHelpers.CreateRole(applicationId: Shop);
        var permission = TestHelpers.CreatePermission(applicationId: Shop, code: "shop:orders:manage");
        var command = Arrange(role, permission, "shop:orders:view");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(PermissionErrors.CannotGrantHigherPermission.Code);
        _permissionRepositoryMock.Verify(
            r => r.GrantToRoleAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _publisherMock.Invocations.Should().BeEmpty();
    }
}

/// <summary>
/// Unit tests for RevokeRolePermissionCommandHandler (OI-226 V1). Only a holder of a
/// code removes it from a role, on every role (D-226-1 (a)); removing <c>*</c> from a
/// platform role never leaves the platform without a full administrator, decided on
/// the grants read under the lock the DELETE runs under; and the event follows the
/// commit. Removal takes no scope check: a role-permission row written before the
/// rule existed (OI-74) stays removable by a holder of <c>*</c>.
/// </summary>
/// <remarks>
/// Deliberate breaks: drop the coverage rule; drop the count; count a grant with an
/// end date; count an account with no password and no external sign-in; publish
/// before the commit. Each turns a row below red.
/// </remarks>
public class RevokeRolePermissionCommandHandlerTests
{
    /// <summary>The seeded Administrator role's codes: every administration code but <c>*</c>.</summary>
    private static readonly string[] AdministratorSet = ["users:*", "roles:*", "permissions:*", "applications:*"];

    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly List<string> _order = [];

    private readonly Role _superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);
    private readonly Permission _wildcard = TestHelpers.CreatePermission(code: "*", level: 0, isWildcard: true);

    /// <summary>
    /// A handler over <paramref name="role"/>, which holds <paramref name="permission"/>,
    /// for the actor holding <paramref name="actorHolds"/>, while
    /// <paramref name="fullAdministrators"/> hold <c>*</c> under the lock.
    /// </summary>
    private (RevokeRolePermissionCommandHandler Handler, Mock<IAccountAuthorityChange> Change) Arrange(
        Role role,
        Permission permission,
        string[] actorHolds,
        params FullAdministratorGrant[] fullAdministrators)
    {
        var policy = AccountRules.Policy(_permissions, _users);
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _permissions
            .Setup(p => p.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { permission });
        _permissions
            .Setup(p => p.GetUserEffectivePermissionsAsync(_actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actorHolds);

        var change = _users.SetupAuthorityChange(fullAdministrators);
        change
            .Setup(c => c.RevokeFromRoleAsync(role.Id, permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        change.Setup(c => c.Commit()).Callback(() => _order.Add("commit"));
        _publisher
            .Setup(p => p.Publish(It.IsAny<RolePermissionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => _order.Add("publish"))
            .Returns(Task.CompletedTask);

        return (new RevokeRolePermissionCommandHandler(
            _roles.Object,
            _permissions.Object,
            policy,
            _publisher.Object,
            new Mock<ILogger<RevokeRolePermissionCommandHandler>>().Object), change);
    }

    private Task<ErrorOr<Success>> RemoveAsync(
        RevokeRolePermissionCommandHandler handler, Role role, Permission permission, CancellationToken? token = null) =>
        handler.Handle(
            new RevokeRolePermissionCommand(role.Id, permission.Id) { RevokedBy = _actor },
            token ?? CancellationToken.None);

    private void ShouldBeRemovedAndPublishedOnce(Mock<IAccountAuthorityChange> change, Role role, Permission permission)
    {
        change.Verify(c => c.RevokeFromRoleAsync(role.Id, permission.Id, It.IsAny<CancellationToken>()), Times.Once());
        change.Verify(c => c.Commit(), Times.Once());
        _publisher.Verify(
            p => p.Publish(
                It.Is<RolePermissionRevokedEvent>(e => e.RoleId == role.Id && e.PermissionId == permission.Id),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    private void ShouldChangeAndPublishNothing(Mock<IAccountAuthorityChange> change)
    {
        change.Verify(c => c.RevokeFromRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        change.Verify(c => c.Commit(), Times.Never());
        _publisher.Invocations.Should().BeEmpty("a refusal publishes nothing");
    }

    // --- T1: only a holder removes ---------------------------------------------

    [Fact]
    public async Task Handle_TheSeededAdministratorSetRemovingTheWildcard_IsRefusedBeforeTheLock()
    {
        var (handler, change) = Arrange(
            _superAdmin, _wildcard, AdministratorSet,
            AccountRules.Wildcard(Guid.NewGuid(), _superAdmin.Id), AccountRules.Wildcard(Guid.NewGuid(), null));

        var result = await RemoveAsync(handler, _superAdmin, _wildcard);

        result.FirstError.Should().Be(PermissionErrors.CannotRemoveHigherPermission);
        ShouldChangeAndPublishNothing(change);
        _users.Verify(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()), Times.Never(),
            "the coverage rule is decided before the lock");
    }

    /// <summary>D-226-1 (a): any role, any code — the remover must hold it.</summary>
    [Theory]
    [InlineData(new[] { "roles:*", "users:*" }, true)]
    [InlineData(new[] { "roles:update", "users:read" }, false)]
    [InlineData(new[] { "roles:update" }, false)]
    public async Task Handle_RemovingACodeFromACustomRole_RequiresHoldingIt(string[] actorHolds, bool allowed)
    {
        var userManager = TestHelpers.CreateRole(code: "user-manager", isSystem: true);
        var usersWildcard = TestHelpers.CreatePermission(code: "users:*", level: 1, isWildcard: true);
        var (handler, change) = Arrange(userManager, usersWildcard, actorHolds);

        var result = await RemoveAsync(handler, userManager, usersWildcard);

        if (allowed)
        {
            result.IsError.Should().BeFalse();
            ShouldBeRemovedAndPublishedOnce(change, userManager, usersWildcard);
        }
        else
        {
            result.FirstError.Should().Be(PermissionErrors.CannotRemoveHigherPermission);
            ShouldChangeAndPublishNothing(change);
        }
    }

    [Fact]
    public async Task Handle_GrantedPermissionOfAnotherScope_IsRemovedByAHolderOfTheWildcard()
    {
        // An application role still holding the platform wildcard: never counted, so
        // its removal takes no full administrator away.
        var applicationRole = TestHelpers.CreateRole(applicationId: Guid.NewGuid());
        var (handler, change) = Arrange(applicationRole, _wildcard, ["*"], AccountRules.Wildcard(_actor, _superAdmin.Id));

        var result = await RemoveAsync(handler, applicationRole, _wildcard);

        result.IsError.Should().BeFalse();
        ShouldBeRemovedAndPublishedOnce(change, applicationRole, _wildcard);
    }

    // --- T2: never the last full administrator ---------------------------------

    [Fact]
    public async Task Handle_RemovingTheWildcardWhileADirectPermanentHolderRemains_IsAllowed()
    {
        var (handler, change) = Arrange(
            _superAdmin, _wildcard, ["*"],
            AccountRules.Wildcard(_actor, _superAdmin.Id), AccountRules.Wildcard(Guid.NewGuid(), null));

        var result = await RemoveAsync(handler, _superAdmin, _wildcard);

        result.IsError.Should().BeFalse();
        ShouldBeRemovedAndPublishedOnce(change, _superAdmin, _wildcard);
    }

    [Fact]
    public async Task Handle_RemovingTheWildcardEveryCountedAdministratorHoldsThroughTheRole_IsRefused()
    {
        var (handler, change) = Arrange(
            _superAdmin, _wildcard, ["*"],
            AccountRules.Wildcard(_actor, _superAdmin.Id), AccountRules.Wildcard(Guid.NewGuid(), _superAdmin.Id));

        var result = await RemoveAsync(handler, _superAdmin, _wildcard);

        result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
        ShouldChangeAndPublishNothing(change);
        change.Verify(c => c.DisposeAsync(), Times.Once(), "a refused change releases its lock at once");
    }

    /// <summary>
    /// T5 and T6: the only other holder must count. One whose <c>*</c> ends at a date,
    /// or with no password and no external sign-in, does not; an external sign-in alone
    /// is a way to authenticate.
    /// </summary>
    [Theory]
    [InlineData(true, false, 60, false)]      // * ends in an hour
    [InlineData(false, false, null, false)]   // no way to authenticate
    [InlineData(false, true, null, true)]     // a linked external sign-in
    public async Task Handle_TheOnlyOtherHolder_CountsOnlyWithACredentialAndWithoutAnEndDate(
        bool hasPassword, bool hasExternalLogin, int? expiresInMinutes, bool allowed)
    {
        var other = AccountRules.Wildcard(
            Guid.NewGuid(), null,
            hasPassword: hasPassword,
            hasExternalLogin: hasExternalLogin,
            expiresAt: expiresInMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);
        var (handler, change) = Arrange(_superAdmin, _wildcard, ["*"], AccountRules.Wildcard(_actor, _superAdmin.Id), other);

        var result = await RemoveAsync(handler, _superAdmin, _wildcard);

        if (allowed)
        {
            result.IsError.Should().BeFalse();
            ShouldBeRemovedAndPublishedOnce(change, _superAdmin, _wildcard);
        }
        else
        {
            result.FirstError.Should().Be(UserErrors.LastFullAdministrator);
            ShouldChangeAndPublishNothing(change);
        }
    }

    // --- T3: one locked path, the event after the commit -----------------------

    [Fact]
    public async Task Handle_ADeleteThatRemovesNothing_AnswersNotGranted_AndPublishesNothing()
    {
        var (handler, change) = Arrange(_superAdmin, _wildcard, ["*"], AccountRules.Wildcard(Guid.NewGuid(), null));
        change
            .Setup(c => c.RevokeFromRoleAsync(_superAdmin.Id, _wildcard.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RemoveAsync(handler, _superAdmin, _wildcard);

        result.FirstError.Should().Be(PermissionErrors.PermissionNotGranted);
        change.Verify(c => c.Commit(), Times.Never(), "nothing was removed, so nothing is committed");
        _publisher.Invocations.Should().BeEmpty("nothing happened here, so nothing is audited");
    }

    [Fact]
    public async Task Handle_TheCallersTokenReachesTheCommit_AndTheEventFollowsItWithNone()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var (handler, change) = Arrange(_superAdmin, _wildcard, ["*"], AccountRules.Wildcard(Guid.NewGuid(), null));

        var result = await RemoveAsync(handler, _superAdmin, _wildcard, token);

        result.IsError.Should().BeFalse();
        _roles.Verify(r => r.GetByIdAsync(_superAdmin.Id, token), Times.Once());
        _permissions.Verify(p => p.GetRolePermissionsAsync(_superAdmin.Id, token), Times.Once());
        _permissions.Verify(p => p.GetUserEffectivePermissionsAsync(_actor, token), Times.Once());
        _users.Verify(u => u.BeginAuthorityChangeAsync(token), Times.Once());
        change.Verify(c => c.RevokeFromRoleAsync(_superAdmin.Id, _wildcard.Id, token), Times.Once());
        _publisher.Verify(p => p.Publish(It.IsAny<RolePermissionRevokedEvent>(), CancellationToken.None), Times.Once(),
            "the permission is gone: a hang-up must not cancel its audit row");
        _order.Should().Equal(["commit", "publish"], "the event is published after the commit, never under the lock");
    }

    [Fact]
    public async Task Handle_PermissionTheRoleDoesNotHold_AnswersNotGranted_WithoutTakingTheLock()
    {
        var (handler, _) = Arrange(_superAdmin, _wildcard, ["*"]);

        var result = await handler.Handle(
            new RevokeRolePermissionCommand(_superAdmin.Id, Guid.NewGuid()) { RevokedBy = _actor }, CancellationToken.None);

        result.FirstError.Should().Be(PermissionErrors.PermissionNotGranted);
        _users.Verify(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>()), Times.Never());
    }
}
