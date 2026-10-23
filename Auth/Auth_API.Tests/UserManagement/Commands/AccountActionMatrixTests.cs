using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Users.ActivateAccount;
using Auth.Application.Features.Users.Common;
using Auth.Application.Features.Users.DeactivateAccount;
using Auth.Application.Features.Users.DeleteUser;
using Auth.Application.Features.Users.GetUserById;
using Auth.Application.Features.Users.GetUserPermissions;
using Auth.Application.Features.Users.GetUserRoles;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Features.Users.HardDeleteUser;
using Auth.Application.Features.Users.LockAccount;
using Auth.Application.Features.Users.RemoveProfileImage;
using Auth.Application.Features.Users.RemoveUserRole;
using Auth.Application.Features.Users.RevokeUserPermission;
using Auth.Application.Features.Users.SetProfileImage;
using Auth.Application.Features.Users.UnlockAccount;
using Auth.Application.Features.Users.UpdateUser;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

/// <summary>
/// OI-224 V1: every administrative action on an account, and the account's own
/// deletion, through its real handler, against the three account rules. A refusal
/// answers its published code, changes nothing and publishes nothing; an allowed
/// action writes.
/// </summary>
/// <remarks>
/// Deliberate breaks (each turns its rows red): drop the policy call from one handler
/// (rules 1 to 3 all go); decide coverage on the role's permissions instead of the
/// account's (the ordinary-account row of RemoveRole: <c>crm:leads:read</c> is not
/// covered by <c>users:*</c>); judge rule 3 on a read the write does not run under
/// (the action rows skew the unlocked read, so a writer that asks
/// <c>EnsureAllowedAsync</c> instead of <c>BeginAsync</c> goes red).
/// </remarks>
public class AccountActionMatrixTests
{
    public enum Scenario
    {
        /// <summary>The target is the system account; the actor holds <c>*</c>.</summary>
        SystemAccount,

        /// <summary>The actor holds <c>users:*</c>; the target holds <c>*</c>.</summary>
        NotCovered,

        /// <summary>Actor and target hold <c>*</c>, and another full administrator can sign in.</summary>
        SecondFullAdministratorRemains,

        /// <summary>
        /// The target is the only full administrator; the actor holds <c>*</c> but cannot
        /// sign in from anywhere now (a timed lock under a live session).
        /// </summary>
        LastFullAdministrator,

        /// <summary>The only full administrator acts on itself.</summary>
        LastFullAdministratorOnItself,

        /// <summary>D1 (a): the actor holds <c>users:*</c>; the target holds no platform permission.</summary>
        OrdinaryAccountByUserAdministrator,
    }

    private static readonly string[] SubjectToRuleThree =
        ["Delete", "PermanentDelete", "RemoveRole", "RevokePermission", "Lock", "Deactivate", "RequestOwnDeletion"];

    private static readonly string[] Actions =
    [
        "Update", "SetProfileImage", "RemoveProfileImage", "Delete", "PermanentDelete", "RemoveRole",
        "RevokePermission", "Lock", "Unlock", "Activate", "Deactivate", "RequestOwnDeletion",
    ];

    /// <summary>Every (action, scenario) the action allows, with the code expected; null when allowed.</summary>
    public static TheoryData<string, Scenario, string?> Matrix()
    {
        var data = new TheoryData<string, Scenario, string?>();
        foreach (var action in Actions)
        {
            var own = action == "RequestOwnDeletion";
            data.Add(action, Scenario.SystemAccount,
                action is "Delete" or "PermanentDelete" or "RequestOwnDeletion" ? "User.CannotDeleteSystemUser" : "User.CannotModifySystemUser");
            if (!own)
            {
                data.Add(action, Scenario.NotCovered, "User.AuthorityNotCovered");
                data.Add(action, Scenario.OrdinaryAccountByUserAdministrator, null);
            }

            data.Add(action, Scenario.SecondFullAdministratorRemains, null);
            if (SubjectToRuleThree.Contains(action))
            {
                var code = action == "RemoveRole" ? "Role.CannotRemoveLastAdmin" : "User.LastFullAdministrator";
                if (!own)
                {
                    data.Add(action, Scenario.LastFullAdministrator, code);
                }

                if (action != "PermanentDelete")
                {
                    data.Add(action, Scenario.LastFullAdministratorOnItself, code);
                }
            }
        }

        return data;
    }

    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IImageStorageService> _storage = new();
    private readonly Mock<IUploadedImageRepository> _uploads = new();
    private readonly Mock<IAccountDeletionRequestRepository> _deletionRequests = new();
    private readonly Mock<IAccountAuthorityChange> _change = new();
    private readonly AccountActionPolicy _policy;

    private readonly Guid _superAdminRole = Guid.NewGuid();
    private readonly Guid _applicationRole = Guid.NewGuid();
    private readonly Guid _wildcardPermission = Guid.NewGuid();
    private readonly Guid _applicationPermission = Guid.NewGuid();
    private readonly Guid _application = Guid.NewGuid();

    private Guid _actor;
    private Guid _target;

    public AccountActionMatrixTests()
    {
        _policy = AccountRules.Policy(_permissions, _users);

        _roles.Setup(r => r.GetByIdAsync(_superAdminRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: _superAdminRole, code: "super-admin", name: "Super Admin"));
        _roles.Setup(r => r.GetByIdAsync(_applicationRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: _applicationRole, applicationId: _application, code: "crm-agent", name: "Agent"));
        _roles.Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _permissions.Setup(p => p.GetByIdAsync(_wildcardPermission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: _wildcardPermission, code: "*", isWildcard: true));
        _permissions.Setup(p => p.GetByIdAsync(_applicationPermission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: _applicationPermission, applicationId: _application, code: "crm:leads:read"));
        // What a role-based (wrong) coverage check would compare: the application role's own codes.
        _permissions.Setup(p => p.GetRolePermissionsAsync(_applicationRole, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(code: "crm:leads:read")]);

        _change.Setup(c => c.RemoveRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _change.Setup(c => c.RevokePermissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _change.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _users.Setup(u => u.BeginAuthorityChangeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_change.Object);
        _users.Setup(u => u.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _uploads.Setup(u => u.TryAttachAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _organizations.Setup(o => o.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Organization>());
        _deletionRequests.Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Handle_AccountRules_RefuseWithTheirCodeAndChangeNothing_OrAllowAndWrite(
        string action, Scenario scenario, string? expectedCode)
    {
        Arrange(action, scenario, skewUnlockedRead: true);

        var result = await RunAsync(action);

        if (expectedCode is null)
        {
            result.IsError.Should().BeFalse($"{action} in {scenario} passes the three rules, but got {(result.IsError ? result.Errors![0].Code : "")}");
            Writes().Should().NotBeEmpty($"{action} was allowed, so it wrote");
            if (SubjectToRuleThree.Contains(action) && action != "PermanentDelete")
            {
                _change.Verify(c => c.Commit(), Times.Once(), $"{action} writes through the locked change and commits it");
            }

            return;
        }

        result.IsError.Should().BeTrue($"{action} in {scenario} is refused");
        result.Errors![0].Code.Should().Be(expectedCode);
        result.Errors![0].Type.Should().Be(ErrorType.Forbidden);
        Writes().Should().BeEmpty($"a refused {action} changes nothing");
        _publisher.Invocations.Should().BeEmpty($"a refused {action} publishes nothing");
    }

    /// <param name="skewUnlockedRead">
    /// For the writes that decide under the lock: the read without a lock reports one
    /// more full administrator than the change's locked read, so a writer that decided
    /// on the unlocked read is allowed where it must be refused (review of PR 101, L3).
    /// The permanent delete decides without the lock by design, so it is never skewed.
    /// </param>
    private void Arrange(string action, Scenario scenario, bool skewUnlockedRead = false)
    {
        var own = action == "RequestOwnDeletion";
        _actor = scenario == Scenario.SystemAccount && own ? WellKnownUserIds.System : Guid.NewGuid();
        _target = scenario switch
        {
            Scenario.SystemAccount => WellKnownUserIds.System,
            Scenario.LastFullAdministratorOnItself => _actor,
            _ when own => _actor,
            _ => Guid.NewGuid(),
        };

        // How the target holds *, when it does: the direct grant for the revocation,
        // the super-admin role otherwise, so each removal takes its only way.
        FullAdministratorGrant TargetWildcard() => AccountRules.Wildcard(
            _target,
            action == "RevokePermission" ? null : _superAdminRole,
            isDeleted: action == "PermanentDelete");

        var actorHolds = scenario is Scenario.NotCovered or Scenario.OrdinaryAccountByUserAdministrator ? new[] { "users:*" } : ["*"];
        var targetHolds = scenario is Scenario.SystemAccount or Scenario.OrdinaryAccountByUserAdministrator ? [] : new[] { "*" };
        var anotherAdministrator = AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole);
        FullAdministratorGrant[] grants = scenario switch
        {
            Scenario.SystemAccount => [AccountRules.Wildcard(_actor, _superAdminRole)],
            Scenario.NotCovered => [TargetWildcard(), anotherAdministrator],
            Scenario.SecondFullAdministratorRemains when _actor == _target => [TargetWildcard(), anotherAdministrator],
            Scenario.SecondFullAdministratorRemains => [TargetWildcard(), AccountRules.Wildcard(_actor, _superAdminRole)],
            Scenario.LastFullAdministrator =>
            [
                TargetWildcard(),
                AccountRules.Wildcard(_actor, _superAdminRole, UserStatus.Locked, failedAttempts: 5, lockoutEndUtc: Now.AddMinutes(10)),
            ],
            Scenario.LastFullAdministratorOnItself => [TargetWildcard()],
            _ => [anotherAdministrator],
        };

        _permissions.Setup(p => p.GetUserEffectivePermissionsAsync(_actor, It.IsAny<CancellationToken>())).ReturnsAsync(actorHolds);
        if (_target != _actor)
        {
            _permissions.Setup(p => p.GetUserEffectivePermissionsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(targetHolds);
        }

        var unlocked = skewUnlockedRead && SubjectToRuleThree.Contains(action) && action != "PermanentDelete"
            ? [.. grants, AccountRules.Wildcard(Guid.NewGuid(), _superAdminRole)]
            : grants;
        _permissions.Setup(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(unlocked);
        _change.SetupGet(c => c.FullAdministratorGrants).Returns(grants);

        var target = TestHelpers.CreateUser(
            id: _target,
            status: action switch
            {
                "Unlock" => UserStatus.Locked,
                "Activate" => UserStatus.Inactive,
                _ => UserStatus.Active,
            },
            isDeleted: action == "PermanentDelete",
            deletedAt: action == "PermanentDelete" ? Now.AddDays(-1) : null);
        _users.Setup(u => u.GetByIdAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        _users.Setup(u => u.GetByIdIncludeDeletedAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(target);
    }

    /// <summary>
    /// OI-224 V6: what the reads tell the console, for every row of the matrix: the
    /// list row's and the account's allowed actions, and each assignment's or grant's
    /// removal, are exactly what the action itself is answered. Deliberate break:
    /// compute one field from another action (UserAllowedActionsDto.For), or with a
    /// copy of a rule that differs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Read_TheCallersAllowedActions_AreWhatTheActionIsAnswered(
        string action, Scenario scenario, string? expectedCode)
    {
        Arrange(action, scenario);
        var allowed = expectedCode is null;
        var images = Mock.Of<IImageUrlComposer>();

        if (action == "RemoveRole")
        {
            var ordinary = scenario is Scenario.OrdinaryAccountByUserAdministrator;
            _users.Setup(u => u.GetUserRolesAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                ordinary
                    ? TestHelpers.CreateUserRole(userId: _target, roleId: _applicationRole, applicationId: _application)
                    : TestHelpers.CreateUserRole(userId: _target, roleId: _superAdminRole),
            ]);
            var roles = await new GetUserRolesQueryHandler(
                    _users.Object, _roles.Object, Mock.Of<IApplicationRepository>(), _policy, Mock.Of<ILogger<GetUserRolesQueryHandler>>())
                .Handle(new GetUserRolesQuery(_target) { ActorId = _actor }, CancellationToken.None);
            roles.Value.Should().ContainSingle().Which.CanRemove.Should().Be(allowed);
            return;
        }

        if (action == "RevokePermission")
        {
            var ordinary = scenario is Scenario.OrdinaryAccountByUserAdministrator;
            _users.Setup(u => u.GetUserPermissionsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                ordinary
                    ? TestHelpers.CreateUserPermission(userId: _target, permissionId: _applicationPermission, applicationId: _application)
                    : TestHelpers.CreateUserPermission(userId: _target, permissionId: _wildcardPermission),
            ]);
            var grants = await new GetUserPermissionsQueryHandler(
                    _users.Object, _permissions.Object, Mock.Of<IApplicationRepository>(), _policy, Mock.Of<ILogger<GetUserPermissionsQueryHandler>>())
                .Handle(new GetUserPermissionsQuery(_target) { ActorId = _actor }, CancellationToken.None);
            grants.Value.Should().ContainSingle().Which.CanRemove.Should().Be(allowed);
            return;
        }

        var target = await _users.Object.GetByIdIncludeDeletedAsync(_target, CancellationToken.None);
        _users.Setup(u => u.GetPagedAsync(1, 20, null, null, It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([target!], 1));
        var page = await new GetUsersQueryHandler(_users.Object, _roles.Object, _permissions.Object, _policy, images)
            .Handle(new GetUsersQuery { ActorId = _actor }, CancellationToken.None);
        var account = await new GetUserByIdQueryHandler(_users.Object, _roles.Object, _permissions.Object, _policy, images)
            .Handle(new GetUserByIdQuery(_target) { ActorId = _actor }, CancellationToken.None);

        foreach (var actions in new[] { page.Value.Users.Single().AllowedActions!, account.Value.AllowedActions! })
        {
            bool? field = action switch
            {
                "Update" => actions.Update,
                "SetProfileImage" or "RemoveProfileImage" => actions.ProfileImage,
                "Delete" => actions.Delete,
                "PermanentDelete" => actions.PermanentDelete,
                "Lock" => actions.Lock,
                "Unlock" => actions.Unlock,
                "Activate" => actions.Activate,
                "Deactivate" => actions.Deactivate,
                "RequestOwnDeletion" => actions.RequestDeletion,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
            };
            field.Should().Be(allowed, $"{action} in {scenario}");
        }

        // The own-deletion flag rides only on one's own account.
        account.Value.AllowedActions!.RequestDeletion.HasValue.Should().Be(_actor == _target);
    }

    private async Task<IErrorOr> RunAsync(string action)
    {
        var ordinary = _target != WellKnownUserIds.System
            && !(await _permissions.Object.GetUserEffectivePermissionsAsync(_target, CancellationToken.None)).Any();
        var guard = new OwnedOrganizationDeletionGuard(_organizations.Object, Mock.Of<ILogger<OwnedOrganizationDeletionGuard>>());
        var ct = CancellationToken.None;

        return action switch
        {
            "Update" => await new UpdateUserCommandHandler(
                    _users.Object, _roles.Object, _permissions.Object, _policy, Mock.Of<ILogger<UpdateUserCommandHandler>>())
                .Handle(new UpdateUserCommand(_target, "First", "Last") { ModifiedBy = _actor }, ct),
            "SetProfileImage" => await new SetProfileImageCommandHandler(_users.Object, _policy, _storage.Object, _uploads.Object)
                .Handle(new SetProfileImageCommand(_target, "images/new.png", _actor), ct),
            "RemoveProfileImage" => await new RemoveProfileImageCommandHandler(_users.Object, _policy, _storage.Object)
                .Handle(new RemoveProfileImageCommand(_target, _actor), ct),
            "Delete" => await new DeleteUserCommandHandler(
                    _users.Object, _policy, guard, _revocation.Object, _publisher.Object, Mock.Of<ILogger<DeleteUserCommandHandler>>())
                .Handle(new DeleteUserCommand(_target) { DeletedBy = _actor }, ct),
            "PermanentDelete" => await new HardDeleteUserCommandHandler(
                    _users.Object, _policy, guard, _revocation.Object, _publisher.Object, Mock.Of<ILogger<HardDeleteUserCommandHandler>>())
                .Handle(new HardDeleteUserCommand(_target) { DeletedBy = _actor }, ct),
            "RemoveRole" => await new RemoveUserRoleCommandHandler(
                    _users.Object, _roles.Object, _policy, _publisher.Object, Mock.Of<ILogger<RemoveUserRoleCommandHandler>>())
                .Handle(ordinary
                    ? new RemoveUserRoleCommand(_target, _applicationRole, _application) { RemovedBy = _actor }
                    : new RemoveUserRoleCommand(_target, _superAdminRole) { RemovedBy = _actor }, ct),
            "RevokePermission" => await new RevokeUserPermissionCommandHandler(
                    _users.Object, _permissions.Object, _policy, _publisher.Object, Mock.Of<ILogger<RevokeUserPermissionCommandHandler>>())
                .Handle(ordinary
                    ? new RevokeUserPermissionCommand(_target, _applicationPermission, _application) { RevokedBy = _actor }
                    : new RevokeUserPermissionCommand(_target, _wildcardPermission) { RevokedBy = _actor }, ct),
            "Lock" => await new LockAccountCommandHandler(
                    _users.Object, _policy, _revocation.Object, _dispatcher.Object, Mock.Of<ILogger<LockAccountCommandHandler>>())
                .Handle(new LockAccountCommand(_target, "Incident", 30, _actor), ct),
            "Unlock" => await new UnlockAccountCommandHandler(
                    _users.Object, _policy, _dispatcher.Object, Mock.Of<ILogger<UnlockAccountCommandHandler>>())
                .Handle(new UnlockAccountCommand(_target, _actor), ct),
            "Activate" => await new ActivateAccountCommandHandler(_users.Object, _policy, Mock.Of<ILogger<ActivateAccountCommandHandler>>())
                .Handle(new ActivateAccountCommand(_target, _actor), ct),
            "Deactivate" => await new DeactivateAccountCommandHandler(
                    _users.Object, _policy, _revocation.Object, Mock.Of<ILogger<DeactivateAccountCommandHandler>>())
                .Handle(new DeactivateAccountCommand(_target, _actor), ct),
            "RequestOwnDeletion" => await new AccountDeletionRequestor(
                    _deletionRequests.Object, _users.Object, _policy, guard, _revocation.Object, _publisher.Object,
                    TestHelpers.CreateOptions(new AccountDeletionSettings()), Mock.Of<ILogger<AccountDeletionRequestor>>())
                .RequestAsync(TestHelpers.CreateUser(id: _target), AccountDeletionSource.InApp, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    /// <summary>Every call that changes state, on any double: the change, the repositories, the side effects.</summary>
    private List<string> Writes()
    {
        string[] changeWrites = ["RemoveRoleAsync", "RevokePermissionAsync", "SetAccountStatusAsync", "DeleteAsync", "Commit"];
        string[] userWrites = ["UpdateAsync", "SetAccountStatusAsync", "DeleteAsync", "HardDeleteAsync", "RemoveRoleAsync", "RevokePermissionAsync"];

        return _change.Invocations.Where(i => changeWrites.Contains(i.Method.Name)).Select(i => "change." + i.Method.Name)
            .Concat(_users.Invocations.Where(i => userWrites.Contains(i.Method.Name)).Select(i => "users." + i.Method.Name))
            .Concat(_revocation.Invocations.Select(i => "revocation." + i.Method.Name))
            .Concat(_dispatcher.Invocations.Select(i => "dispatcher." + i.Method.Name))
            .Concat(_uploads.Invocations.Select(i => "uploads." + i.Method.Name))
            .Concat(_storage.Invocations.Select(i => "storage." + i.Method.Name))
            .Concat(_deletionRequests.Invocations.Where(i => i.Method.Name == "TryCreateAsync").Select(i => "requests.TryCreateAsync"))
            .Concat(_organizations.Invocations.Where(i => i.Method.Name == "DeleteAsync").Select(i => "organizations.DeleteAsync"))
            .ToList();
    }
}
