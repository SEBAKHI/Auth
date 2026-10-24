using Auth.Application.Common;
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
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly AssignRoleCommandHandler _handler;

    public AssignRoleCommandHandlerTests()
    {
        // The actor holds "*" unless a test says otherwise: these cases are
        // about assignment mechanics, not about the no-amplification rule,
        // which has its own suite.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["*"]);
        // Roles carry nothing here. Set explicitly rather than left to Moq's
        // default, which is null: the handler is deliberately NOT null-tolerant,
        // because a guard that treats a failed lookup as "no permissions to
        // check" fails open.
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _handler = new AssignRoleCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            TestHelpers.CreatePlatformGrantFactorGuard(),
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

/// <summary>
/// An assignment is (user, role, scope). Removing an application-scoped one used to
/// pass a scope-blind existence check, miss it with a platform-scoped DELETE, and be
/// reported, published and audited all the same. These pin the scope the command
/// carries all the way to the DELETE, and that nothing is reported when the DELETE
/// removed nothing.
/// </summary>
public class RemoveUserRoleCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<IAccountAuthorityChange> _change;
    private readonly RemoveUserRoleCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    public RemoveUserRoleCommandHandlerTests()
    {
        _change = _userRepositoryMock.SetupAuthorityChange();
        _handler = new RemoveUserRoleCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            AccountRules.Policy(users: _userRepositoryMock),
            _publisherMock.Object,
            new Mock<ILogger<RemoveUserRoleCommandHandler>>().Object);

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: _userId));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(_roleId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole(id: _roleId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_RemovesTheAssignmentAtTheScopeItNames_AndReportsThatScope(bool applicationScoped)
    {
        Guid? applicationId = applicationScoped ? Guid.NewGuid() : null;
        var removedBy = Guid.NewGuid();
        // Only the exact scope answers true: any other reaches Moq's default, false.
        _change
            .Setup(r => r.RemoveRoleAsync(_userId, _roleId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new RemoveUserRoleCommand(_userId, _roleId, applicationId) { RemovedBy = removedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _change.Verify(
            r => r.RemoveRoleAsync(_userId, _roleId, applicationId, It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<UserRoleRemovedEvent>(e =>
                    e.UserId == _userId && e.RoleId == _roleId && e.ApplicationId == applicationId && e.RemovedBy == removedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <remarks>
    /// The reported case: the assignment exists at another scope, so the DELETE at the
    /// named one removes nothing. Deliberate break: ignore the repository's answer.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheDeleteRemovedNothing_ReturnsNotAssigned_AndNeitherPublishesNorAudits()
    {
        _change
            .Setup(r => r.RemoveRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            new RemoveUserRoleCommand(_userId, _roleId) { RemovedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Role.NotAssigned");
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _publisherMock.Invocations.Should().BeEmpty(
            "UserRoleRemovedEvent is what the audit log records; publishing it for a removal that did not happen is the defect");
    }

    /// <remarks>
    /// A removal is not retryable (a retry answers 404), so once the DELETE removed the
    /// row a hang-up must not cancel the audit insert. Deliberate break: pass the
    /// request's token to Publish again.
    /// </remarks>
    [Fact]
    public async Task Handle_AfterTheDeleteRemovedTheRow_PublishesWithATokenTheCallerCannotCancel()
    {
        using var hungUp = new CancellationTokenSource();
        hungUp.Cancel();
        var published = hungUp.Token;
        _change
            .Setup(r => r.RemoveRoleAsync(_userId, _roleId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _publisherMock
            .Setup(p => p.Publish(It.IsAny<UserRoleRemovedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserRoleRemovedEvent, CancellationToken>((_, token) => published = token)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(
            new RemoveUserRoleCommand(_userId, _roleId) { RemovedBy = Guid.NewGuid() },
            hungUp.Token);

        result.IsError.Should().BeFalse();
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<UserRoleRemovedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
        published.CanBeCanceled.Should().BeFalse(
            "the assignment is already gone, so the audit row must be written whatever the caller does");
    }

    [Fact]
    public async Task Handle_UnknownRole_ReturnsNotFound_WithoutDeleting()
    {
        var result = await _handler.Handle(
            new RemoveUserRoleCommand(_userId, Guid.NewGuid()) { RemovedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _change.Verify(
            r => r.RemoveRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}

public class GrantUserPermissionCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly GrantUserPermissionCommandHandler _handler;

    public GrantUserPermissionCommandHandlerTests()
    {
        // The actor holds "*" unless a test says otherwise — see the note on
        // AssignRoleCommandHandlerTests.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["*"]);

        _handler = new GrantUserPermissionCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            TestHelpers.CreatePlatformGrantFactorGuard(),
            new Mock<IPublisher>().Object,
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

/// <summary>
/// The same defect as role removal, on direct grants: a grant is (user, permission,
/// scope), the API grants at an application's scope, and the revocation used to pass
/// a scope-blind check, miss with a platform-scoped DELETE, and be audited anyway.
/// </summary>
public class RevokeUserPermissionCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<IAccountAuthorityChange> _change;
    private readonly RevokeUserPermissionCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _permissionId = Guid.NewGuid();

    public RevokeUserPermissionCommandHandlerTests()
    {
        _change = _userRepositoryMock.SetupAuthorityChange();
        _handler = new RevokeUserPermissionCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            AccountRules.Policy(users: _userRepositoryMock),
            _publisherMock.Object,
            new Mock<ILogger<RevokeUserPermissionCommandHandler>>().Object);

        _userRepositoryMock.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: _userId));
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(_permissionId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreatePermission(id: _permissionId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_RevokesTheGrantAtTheScopeItNames_AndReportsThatScope(bool applicationScoped)
    {
        Guid? applicationId = applicationScoped ? Guid.NewGuid() : null;
        var revokedBy = Guid.NewGuid();
        // Only the exact scope answers true: any other reaches Moq's default, false.
        _change
            .Setup(r => r.RevokePermissionAsync(_userId, _permissionId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(_userId, _permissionId, applicationId) { RevokedBy = revokedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _change.Verify(
            r => r.RevokePermissionAsync(_userId, _permissionId, applicationId, It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<UserPermissionRevokedEvent>(e =>
                    e.UserId == _userId && e.PermissionId == _permissionId && e.ApplicationId == applicationId && e.RevokedBy == revokedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <remarks>Deliberate break: ignore the repository's answer.</remarks>
    [Fact]
    public async Task Handle_WhenTheDeleteRevokedNothing_ReturnsNotGranted_AndNeitherPublishesNorAudits()
    {
        _change
            .Setup(r => r.RevokePermissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(_userId, _permissionId) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.NotGranted");
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _publisherMock.Invocations.Should().BeEmpty(
            "UserPermissionRevokedEvent is what the audit log records; publishing it for a revocation that did not happen is the defect");
    }

    /// <remarks>Deliberate break: pass the request's token to Publish again.</remarks>
    [Fact]
    public async Task Handle_AfterTheDeleteRevokedTheGrant_PublishesWithATokenTheCallerCannotCancel()
    {
        using var hungUp = new CancellationTokenSource();
        hungUp.Cancel();
        var published = hungUp.Token;
        _change
            .Setup(r => r.RevokePermissionAsync(_userId, _permissionId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _publisherMock
            .Setup(p => p.Publish(It.IsAny<UserPermissionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserPermissionRevokedEvent, CancellationToken>((_, token) => published = token)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(_userId, _permissionId) { RevokedBy = Guid.NewGuid() },
            hungUp.Token);

        result.IsError.Should().BeFalse();
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<UserPermissionRevokedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
        published.CanBeCanceled.Should().BeFalse(
            "the grant is already gone, so the audit row must be written whatever the caller does");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        var result = await _handler.Handle(
            new RevokeUserPermissionCommand(Guid.NewGuid(), _permissionId) { RevokedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        _change.Verify(
            r => r.RevokePermissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
