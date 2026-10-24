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
    private readonly Mock<IApplicationAccessRepository> _accessRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly AssignRoleCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: "crm");

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
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_application);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId));

        _handler = new AssignRoleCommandHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            TestHelpers.CreatePlatformGrantFactorGuard(),
            TestHelpers.CreateApplicationAccessWriter(_accessRepositoryMock, _publisherMock),
            _publisherMock.Object,
            new Mock<ILogger<AssignRoleCommandHandler>>().Object);
    }

    private Role ApplicationRole(string code = "crm-agent")
    {
        var role = TestHelpers.CreateRole(applicationId: _application.Id, code: code, name: "Agent");
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        return role;
    }

    private void ActorHolds(params string[] codes) =>
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);

    private Task<ErrorOr<Success>> Assign(Guid roleId) =>
        _handler.Handle(new AssignRoleCommand(_userId, roleId) { AssignedBy = _actorId }, CancellationToken.None);

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

    // ── OI-223 T5 (D-100-2 (b)): an application-owned role from the user page ──

    /// <remarks>Deliberate break: write the role with the request's scope (null) again.</remarks>
    [Fact]
    public async Task AnApplicationOwnedRole_IsAssignedAtItsApplication_WithAccess()
    {
        var role = ApplicationRole();
        var order = new List<string>();
        _accessRepositoryMock
            .Setup(r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("access"));
        _roleRepositoryMock
            .Setup(r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("role"));

        var result = await Assign(role.Id);

        result.IsError.Should().BeFalse();
        _roleRepositoryMock.Verify(
            r => r.AssignToUserAsync(It.Is<UserRole>(ur => ur.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(
                It.Is<ApplicationUserAccess>(a => a.ApplicationId == _application.Id && a.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once());
        order.Should().Equal("access", "role");
        _publisherMock.Verify(
            p => p.Publish(It.Is<RoleAssignedEvent>(e => e.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(It.Is<ApplicationAccessGrantedEvent>(e => e.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <remarks>
    /// Deliberate break: drop applications:update from the guard's codes. Covering the
    /// role's codes is not enough: authority inside an application is what the
    /// application page gates with applications:update (R1).
    /// </remarks>
    [Fact]
    public async Task AnApplicationOwnedRole_WithoutApplicationsUpdate_IsRefused_AndNothingIsWritten()
    {
        var role = ApplicationRole();
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(applicationId: _application.Id, code: "crm:leads:read")]);
        ActorHolds("crm:leads:read", "users:manage-roles");

        var result = await Assign(role.Id);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        _roleRepositoryMock.Verify(r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never());
        _accessRepositoryMock.Invocations.Should().BeEmpty("no access is read or written before every check passed");
        _publisherMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AnApplicationOwnedRole_WithApplicationsUpdate_AndTheRolesCodes_IsAssigned()
    {
        var role = ApplicationRole();
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(applicationId: _application.Id, code: "crm:leads:read")]);
        ActorHolds("crm:leads:read", "applications:update");

        var result = await Assign(role.Id);

        result.IsError.Should().BeFalse();
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(_actorId, It.IsAny<CancellationToken>()), Times.Once(),
            "one guard read covers the role's codes and applications:update together");
    }

    [Fact]
    public async Task AGlobalRole_StaysAtPlatformScope_WithNoAccessWrite()
    {
        var role = TestHelpers.CreateRole(name: "Support");
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        ActorHolds("users:manage-roles");

        var result = await Assign(role.Id);

        result.IsError.Should().BeFalse("a global role asks for no applications:update");
        _roleRepositoryMock.Verify(
            r => r.AssignToUserAsync(It.Is<UserRole>(ur => ur.ApplicationId == null), It.IsAny<CancellationToken>()),
            Times.Once());
        _accessRepositoryMock.Invocations.Should().BeEmpty();
        _publisherMock.Verify(
            p => p.Publish(It.Is<RoleAssignedEvent>(e => e.ApplicationId == null), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task ALegacyUnscopedRowOfTheSameApplicationRole_DoesNotBlockTheScopedAssignment()
    {
        var role = ApplicationRole();
        _userRepositoryMock
            .Setup(r => r.GetUserRoleAsync(_userId, role.Id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserRole.Create(_userId, role.Id, _actorId));

        var result = await Assign(role.Id);

        result.IsError.Should().BeFalse();
        _roleRepositoryMock.Verify(
            r => r.AssignToUserAsync(It.Is<UserRole>(ur => ur.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task AValidScopedAssignment_IsTheDuplicate_AndNothingIsWritten()
    {
        var role = ApplicationRole();
        _userRepositoryMock
            .Setup(r => r.GetUserRoleAsync(_userId, role.Id, _application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserRole.Create(_userId, role.Id, _actorId, _application.Id));

        var result = await Assign(role.Id);

        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        _roleRepositoryMock.Verify(r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never());
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
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IApplicationAccessRepository> _accessRepositoryMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly GrantUserPermissionCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: "crm");
    private readonly Auth.Domain.Entities.Application _otherApplication = TestHelpers.CreateApplication(code: "app-a");

    public GrantUserPermissionCommandHandlerTests()
    {
        // The actor holds "*" unless a test says otherwise — see the note on
        // AssignRoleCommandHandlerTests.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["*"]);
        foreach (var application in new[] { _application, _otherApplication })
        {
            _applicationRepositoryMock
                .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
        }

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId));

        _handler = new GrantUserPermissionCommandHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            new PermissionGrantGuard(_permissionRepositoryMock.Object),
            TestHelpers.CreatePlatformGrantFactorGuard(),
            TestHelpers.CreateApplicationAccessWriter(_accessRepositoryMock, _publisherMock),
            _publisherMock.Object,
            new Mock<ILogger<GrantUserPermissionCommandHandler>>().Object);
    }

    private Permission PlatformPermission(string code = "users:read")
    {
        var permission = TestHelpers.CreatePermission(code: code);
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        return permission;
    }

    private Permission ApplicationPermission(string code = "crm:leads:read")
    {
        var permission = TestHelpers.CreatePermission(applicationId: _application.Id, code: code);
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        return permission;
    }

    private void ExistingGrant(Permission permission, Guid? scope, DateTime? expiresAt = null) =>
        _userRepositoryMock
            .Setup(r => r.GetUserPermissionAsync(_userId, permission.Id, scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserPermission.Create(_userId, permission.Id, _actorId, scope, expiresAt));

    private void ActorHolds(params string[] codes) =>
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);

    private Task<ErrorOr<bool>> Grant(Permission permission, Guid? applicationId = null) =>
        _handler.Handle(
            new GrantUserPermissionCommand(_userId, permission.Id, applicationId) { GrantedBy = _actorId },
            CancellationToken.None);

    private void VerifyGranted(Permission permission, Guid? scope) =>
        _userRepositoryMock.Verify(
            r => r.GrantPermissionAsync(
                It.Is<UserPermission>(p => p.UserId == _userId && p.PermissionId == permission.Id && p.ApplicationId == scope),
                It.IsAny<CancellationToken>()),
            Times.Once());

    private void VerifyNothingWritten()
    {
        _userRepositoryMock.Verify(r => r.GrantPermissionAsync(It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()), Times.Never());
        _accessRepositoryMock.Invocations.Should().BeEmpty("no access is read or written before every check passed");
        _publisherMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidGrant_GrantsPermission()
    {
        var permission = PlatformPermission();

        var result = await Grant(permission);

        result.IsError.Should().BeFalse();
        VerifyGranted(permission, scope: null);
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

    // ── OI-223 T1: the duplicate is read by the full key ──

    /// <remarks>
    /// The reported case: a grant at one scope answered 409 to the same permission at
    /// the other. Deliberate break: read the duplicate at the platform scope whatever
    /// the grant's scope.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AValidGrantAtTheOtherScope_IsNoDuplicate(bool existingAtPlatform)
    {
        var permission = PlatformPermission();
        Guid? existing = existingAtPlatform ? null : _application.Id;
        Guid? requested = existingAtPlatform ? _application.Id : null;
        ExistingGrant(permission, existing);

        var result = await Grant(permission, requested);

        result.IsError.Should().BeFalse();
        VerifyGranted(permission, requested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AValidGrantAtTheSameScope_IsTheDuplicate_AndNothingIsWritten(bool atPlatform)
    {
        var permission = PlatformPermission();
        Guid? scope = atPlatform ? null : _application.Id;
        ExistingGrant(permission, scope);

        var result = await Grant(permission, scope);

        result.FirstError.Code.Should().Be("Permission.AlreadyGranted");
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        VerifyNothingWritten();
    }

    /// <remarks>
    /// An expired grant keeps its row; the write restores it (the repository's
    /// update-or-insert, pinned by UserPermissionGrantSqlTests). Deliberate break:
    /// refuse on any existing row, valid or not.
    /// </remarks>
    [Fact]
    public async Task AnExpiredGrantAtTheSameScope_IsGrantedAgain_AndPublishedOnce()
    {
        var permission = PlatformPermission();
        ExistingGrant(permission, scope: null, expiresAt: DateTime.UtcNow.AddDays(-1));

        var result = await Grant(permission);

        result.IsError.Should().BeFalse();
        VerifyGranted(permission, scope: null);
        _publisherMock.Verify(
            p => p.Publish(It.Is<UserPermissionGrantedEvent>(e => e.PermissionId == permission.Id), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    // ── OI-223 T3: the scope a grant lands in (D-100-4 (a), OI-80 (1), R1) ──

    /// <remarks>
    /// The console sends no application (null); the API may send the owner. Both land
    /// at the owner, with access, and access is written before the grant.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnApplicationOwnedPermission_IsGrantedAtItsApplication_WithAccess(bool requestNamesTheOwner)
    {
        var permission = ApplicationPermission();
        var order = new List<string>();
        _accessRepositoryMock
            .Setup(r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("access"));
        _userRepositoryMock
            .Setup(r => r.GrantPermissionAsync(It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("grant"))
            .ReturnsAsync((UserPermission p, CancellationToken _) => p);

        var result = await Grant(permission, requestNamesTheOwner ? _application.Id : null);

        result.IsError.Should().BeFalse();
        VerifyGranted(permission, _application.Id);
        order.Should().Equal("access", "grant");
        _publisherMock.Verify(
            p => p.Publish(It.Is<UserPermissionGrantedEvent>(e => e.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(It.Is<ApplicationAccessGrantedEvent>(e => e.ApplicationId == _application.Id && e.UserId == _userId), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task AnApplicationOwnedPermission_InsideAnotherApplication_IsNotFound_AndNothingIsWritten()
    {
        var permission = ApplicationPermission();

        var result = await Grant(permission, _otherApplication.Id);

        result.FirstError.Code.Should().Be("Permission.NotFound");
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task APlatformPermission_InsideAnApplicationThatDoesNotExist_IsNotFound_AndNothingIsWritten()
    {
        var permission = PlatformPermission();

        var result = await Grant(permission, Guid.NewGuid());

        result.FirstError.Code.Should().Be("Application.NotFound");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task APlatformPermission_WithNoScope_StaysAtThePlatform_WithNoAccessWrite()
    {
        var permission = PlatformPermission();
        ActorHolds("users:read");

        var result = await Grant(permission);

        result.IsError.Should().BeFalse("a platform grant asks for no applications:update");
        VerifyGranted(permission, scope: null);
        _accessRepositoryMock.Invocations.Should().BeEmpty();
        _applicationRepositoryMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task APlatformPermission_InsideAnApplication_IsGrantedThere_WithAccess()
    {
        var permission = PlatformPermission();

        var result = await Grant(permission, _application.Id);

        result.IsError.Should().BeFalse();
        VerifyGranted(permission, _application.Id);
        _accessRepositoryMock.Verify(
            r => r.CreateGrantAsync(It.Is<ApplicationUserAccess>(a => a.ApplicationId == _application.Id), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <remarks>
    /// R1, R2: any write at an application's scope asks applications:update with the
    /// permission's code, in one guard read, and refuses with the existing code.
    /// Deliberate breaks: drop applications:update from the guard's codes; write the
    /// access before the guard.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnyApplicationScope_WithoutApplicationsUpdate_IsRefused_AndNothingIsWritten(bool applicationOwned)
    {
        var permission = applicationOwned ? ApplicationPermission() : PlatformPermission();
        ActorHolds(permission.Code.Value, "users:manage-permissions");

        var result = await Grant(permission, applicationOwned ? null : _application.Id);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        VerifyNothingWritten();
    }

    [Fact]
    public async Task AnApplicationScope_WithApplicationsUpdate_AndTheCode_IsGranted_InOneGuardRead()
    {
        var permission = ApplicationPermission();
        ActorHolds(permission.Code.Value, "applications:update");

        var result = await Grant(permission);

        result.IsError.Should().BeFalse();
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(_actorId, It.IsAny<CancellationToken>()), Times.Once());
    }

    // ── OI-223 T4: the access the grant carries ──

    [Fact]
    public async Task AValidAccessGrant_IsLeftUntouched_ItsExpiryIncluded()
    {
        var permission = ApplicationPermission();
        var expiry = DateTime.UtcNow.AddDays(30);
        var access = ApplicationUserAccess.Create(_application.Id, _userId, _actorId, expiry);
        _accessRepositoryMock
            .Setup(r => r.GetGrantAsync(_application.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);

        var result = await _handler.Handle(
            new GrantUserPermissionCommand(_userId, permission.Id, ExpiresAt: DateTime.UtcNow.AddDays(1)) { GrantedBy = _actorId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        access.ExpiresAt.Should().Be(expiry);
        _accessRepositoryMock.Verify(r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        _accessRepositoryMock.Verify(r => r.UpdateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(p => p.Publish(It.IsAny<ApplicationAccessGrantedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
        VerifyGranted(permission, _application.Id);
    }

    /// <remarks>Deliberate break: always create the access grant.</remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALapsedOrRevokedAccessGrant_IsReinstatedOnItsRow_AndPublished(bool revoked)
    {
        var permission = ApplicationPermission();
        var access = ApplicationUserAccess.Create(
            _application.Id, _userId, _actorId, revoked ? null : DateTime.UtcNow.AddDays(-1));
        if (revoked)
        {
            access.Revoke(_actorId);
        }

        _accessRepositoryMock
            .Setup(r => r.GetGrantAsync(_application.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);

        var result = await Grant(permission);

        result.IsError.Should().BeFalse();
        access.IsValid().Should().BeTrue();
        _accessRepositoryMock.Verify(r => r.UpdateGrantAsync(access, It.IsAny<CancellationToken>()), Times.Once());
        _accessRepositoryMock.Verify(r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(p => p.Publish(It.IsAny<ApplicationAccessGrantedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
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
