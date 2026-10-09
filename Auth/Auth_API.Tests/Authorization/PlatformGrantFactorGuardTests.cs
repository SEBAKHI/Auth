using Auth.Application.Common;
using Auth.Application.Features.Roles.GrantRolePermission;
using Auth.Application.Features.Users.AssignRole;
using Auth.Application.Features.Users.GrantUserPermission;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// S08 PR B, deviation row 88 (a), design D-B6: while
/// <c>TwoFactor:EnforceForPlatformAdmins</c> is on, platform authority goes only to
/// an account that already has its own second factor — through each of the three
/// paths that hand it to an account. Application- and organization-scope grants
/// are never touched, and with enforcement off the factor is not even read.
/// </summary>
public class PlatformGrantFactorGuardTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    private readonly Mock<IPlatformMfaPolicy> _policy = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();

    private PlatformGrantFactorGuard Guard() =>
        new(_policy.Object, _store.Object, Mock.Of<ILogger<PlatformGrantFactorGuard>>());

    private void Enforcing(bool on) => _policy.SetupGet(p => p.IsEnforcing).Returns(on);

    private void TargetHasFactor(bool has) =>
        _store.Setup(s => s.HasEnabledFactorAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(has);

    private void VerifyNoFactorRead()
    {
        _store.Verify(s => s.HasEnabledFactorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.HasPlatformRoleHolderWithoutFactorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Role PlatformRole(Guid? id = null) => TestHelpers.CreateRole(id: id ?? Guid.NewGuid(), name: "operators");

    private static Role ApplicationRole() =>
        TestHelpers.CreateRole(id: Guid.NewGuid(), name: "exhibitor", applicationId: Guid.NewGuid());

    private static Permission ActivePermission(string code = "users:read") =>
        TestHelpers.CreatePermission(id: Guid.NewGuid(), code: code);

    // ── The guard itself ───────────────────────────────────────────────────

    [Fact]
    public async Task EnforcementOff_ReadsNothing_AndAllows()
    {
        Enforcing(false);

        (await Guard().EnsureAccountMayReceiveAsync(Target, true, [ActivePermission()], CancellationToken.None))
            .IsError.Should().BeFalse();
        (await Guard().EnsureRoleHoldersMayReceiveAsync(PlatformRole(), ActivePermission(), CancellationToken.None))
            .IsError.Should().BeFalse();

        VerifyNoFactorRead();
    }

    [Fact]
    public async Task Enforcing_AccountWithoutFactor_IsRefused()
    {
        Enforcing(true);
        TargetHasFactor(false);

        var result = await Guard().EnsureAccountMayReceiveAsync(Target, true, [ActivePermission()], CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredForPlatformGrant.Code);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
    }

    [Fact]
    public async Task Enforcing_AccountWithFactor_IsAllowed()
    {
        Enforcing(true);
        TargetHasFactor(true);

        (await Guard().EnsureAccountMayReceiveAsync(Target, true, [ActivePermission()], CancellationToken.None))
            .IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Enforcing_ApplicationScope_OrNoActivePermission_IsNeverTouched()
    {
        Enforcing(true);
        TargetHasFactor(false);

        (await Guard().EnsureAccountMayReceiveAsync(Target, false, [ActivePermission()], CancellationToken.None))
            .IsError.Should().BeFalse("an application grant reaches no platform token: two-step stays optional there");
        (await Guard().EnsureAccountMayReceiveAsync(Target, true, [], CancellationToken.None))
            .IsError.Should().BeFalse("a role with no permissions hands over no authority");
        (await Guard().EnsureRoleHoldersMayReceiveAsync(ApplicationRole(), ActivePermission(), CancellationToken.None))
            .IsError.Should().BeFalse();

        VerifyNoFactorRead();
    }

    [Fact]
    public async Task Enforcing_RoleWithAHolderWithoutFactor_IsRefused_InOneRead()
    {
        Enforcing(true);
        var role = PlatformRole();
        _store.Setup(s => s.HasPlatformRoleHolderWithoutFactorAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Guard().EnsureRoleHoldersMayReceiveAsync(role, ActivePermission(), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredForPlatformGrant.Code);
        _store.Verify(s => s.HasPlatformRoleHolderWithoutFactorAsync(role.Id, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.HasEnabledFactorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "one query for every holder, never one read per account");
    }

    // ── The three grant paths ──────────────────────────────────────────────

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();

    private void ActorHoldsEverything() =>
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>())).ReturnsAsync(["*"]);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task AssignRole_AtPlatformScope(bool enforcing, bool targetHasFactor, bool refused)
    {
        Enforcing(enforcing);
        TargetHasFactor(targetHasFactor);
        ActorHoldsEverything();
        var role = PlatformRole();
        _users.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: Target));
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _permissions.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync([ActivePermission()]);

        var handler = new AssignRoleCommandHandler(
            _users.Object, _roles.Object, Mock.Of<IApplicationRepository>(), _permissions.Object,
            new PermissionGrantGuard(_permissions.Object), Guard(), Mock.Of<IPublisher>(),
            Mock.Of<ILogger<AssignRoleCommandHandler>>());

        var result = await handler.Handle(new AssignRoleCommand(Target, role.Id) { AssignedBy = Actor }, CancellationToken.None);

        result.IsError.Should().Be(refused);
        if (refused)
        {
            result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredForPlatformGrant.Code);
            _roles.Verify(r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        if (!enforcing)
        {
            VerifyNoFactorRead();
        }
    }

    [Fact]
    public async Task AssignRole_ScopedToAnApplication_IsNeverTouched()
    {
        Enforcing(true);
        TargetHasFactor(false);
        ActorHoldsEverything();
        var role = PlatformRole();
        var applicationId = Guid.NewGuid();
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId));
        _users.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: Target));
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _permissions.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync([ActivePermission()]);

        var handler = new AssignRoleCommandHandler(
            _users.Object, _roles.Object, applications.Object, _permissions.Object,
            new PermissionGrantGuard(_permissions.Object), Guard(), Mock.Of<IPublisher>(),
            Mock.Of<ILogger<AssignRoleCommandHandler>>());

        var result = await handler.Handle(
            new AssignRoleCommand(Target, role.Id, ApplicationId: applicationId) { AssignedBy = Actor }, CancellationToken.None);

        result.IsError.Should().BeFalse("an application-scope assignment reaches no platform token");
        VerifyNoFactorRead();
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task GrantUserPermission_AtPlatformScope(bool enforcing, bool targetHasFactor, bool refused)
    {
        Enforcing(enforcing);
        TargetHasFactor(targetHasFactor);
        ActorHoldsEverything();
        var permission = ActivePermission();
        _users.Setup(r => r.GetByIdAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: Target));
        _permissions.Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);

        var handler = new GrantUserPermissionCommandHandler(
            _users.Object, _permissions.Object, new PermissionGrantGuard(_permissions.Object), Guard(),
            Mock.Of<IPublisher>(), Mock.Of<ILogger<GrantUserPermissionCommandHandler>>());

        var result = await handler.Handle(new GrantUserPermissionCommand(Target, permission.Id) { GrantedBy = Actor }, CancellationToken.None);

        result.IsError.Should().Be(refused);
        if (refused)
        {
            result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredForPlatformGrant.Code);
            _users.Verify(r => r.GrantPermissionAsync(It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        if (!enforcing)
        {
            VerifyNoFactorRead();
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task GrantRolePermission_ToAPlatformRole(bool enforcing, bool holderWithoutFactor, bool refused)
    {
        Enforcing(enforcing);
        ActorHoldsEverything();
        var role = PlatformRole();
        var permission = ActivePermission();
        _store.Setup(s => s.HasPlatformRoleHolderWithoutFactorAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(holderWithoutFactor);
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _permissions.Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        _permissions.Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var handler = new GrantRolePermissionCommandHandler(
            _roles.Object, _permissions.Object, new PermissionGrantGuard(_permissions.Object), Guard(),
            Mock.Of<IPublisher>(), Mock.Of<ILogger<GrantRolePermissionCommandHandler>>());

        var result = await handler.Handle(new GrantRolePermissionCommand(role.Id, permission.Id) { GrantedBy = Actor }, CancellationToken.None);

        result.IsError.Should().Be(refused);
        if (refused)
        {
            result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredForPlatformGrant.Code);
            _permissions.Verify(r => r.GrantToRoleAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        if (!enforcing)
        {
            VerifyNoFactorRead();
        }
    }
}
