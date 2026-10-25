using Auth.Application.Features.Roles.GetRoleById;
using Auth.Application.Features.Roles.GetRoles;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
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
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly GetRoleByIdQueryHandler _handler;

    public GetRoleByIdQueryHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();

        _handler = new GetRoleByIdQueryHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _userRepositoryMock.Object,
            AccountRules.Policy(_permissionRepositoryMock, _userRepositoryMock));
    }

    /// <summary>The seeded Administrator role's codes: every administration code but <c>*</c>.</summary>
    private static readonly string[] AdministratorSet = ["users:*", "roles:*", "permissions:*", "applications:*"];

    private readonly Guid _actor = Guid.NewGuid();

    /// <summary>
    /// Reads <paramref name="role"/>, holding <paramref name="codes"/>, as the actor who
    /// holds <paramref name="actorHolds"/>, while <paramref name="fullAdministrators"/>
    /// hold <c>*</c>.
    /// </summary>
    private async Task<RoleDto> ReadAsync(
        Role role, string[] codes, string[] actorHolds, params FullAdministratorGrant[] fullAdministrators)
    {
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(code => TestHelpers.CreatePermission(code: code)).ToList());
        _permissionRepositoryMock
            .Setup(p => p.GetUserEffectivePermissionsAsync(_actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actorHolds);
        _permissionRepositoryMock
            .Setup(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullAdministrators);

        var result = await _handler.Handle(new GetRoleByIdQuery(role.Id) { ActorId = _actor }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        return result.Value;
    }

    /// <summary>
    /// OI-226 V6: what the role read offers to remove is what the removal answers,
    /// computed through the policy. With the seeded Administrator set, <c>*</c> on the
    /// super-admin role is not offered and <c>users:*</c> on the user-manager role is.
    /// Deliberate break: compute the field with a copied rule that ignores the count →
    /// the last-administrator rows go red.
    /// </summary>
    [Fact]
    public async Task Handle_ForTheSeededAdministratorSet_OffersOnlyTheCodesItHolds()
    {
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);
        var userManager = TestHelpers.CreateRole(code: "user-manager", isSystem: true);
        var holders = new[] { AccountRules.Wildcard(Guid.NewGuid(), superAdmin.Id), AccountRules.Wildcard(Guid.NewGuid(), null) };

        var onSuperAdmin = await ReadAsync(superAdmin, ["*"], AdministratorSet, holders);
        var onUserManager = await ReadAsync(userManager, ["users:*", "roles:read"], AdministratorSet, holders);

        onSuperAdmin.RemovablePermissions.Should().BeEmpty();
        onUserManager.RemovablePermissions.Should().BeEquivalentTo("users:*", "roles:read");
    }

    [Fact]
    public async Task Handle_ForTheLastFullAdministrator_DoesNotOfferTheWildcard()
    {
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);

        var dto = await ReadAsync(superAdmin, ["*", "users:read"], ["*"], AccountRules.Wildcard(_actor, superAdmin.Id));

        dto.RemovablePermissions.Should().BeEquivalentTo("users:read");
    }

    /// <summary>
    /// The other holder decides: a permanent, credentialed one lets <c>*</c> go; one
    /// whose <c>*</c> ends at a date, or with no way to authenticate, does not (T5, T6).
    /// </summary>
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, 60, false)]
    [InlineData(false, null, false)]
    public async Task Handle_WithASecondHolder_OffersTheWildcardOnlyIfThatHolderCounts(
        bool hasPassword, int? expiresInMinutes, bool offered)
    {
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);
        var other = AccountRules.Wildcard(
            Guid.NewGuid(), null,
            hasPassword: hasPassword,
            expiresAt: expiresInMinutes is { } minutes ? DateTime.UtcNow.AddMinutes(minutes) : null);

        var dto = await ReadAsync(superAdmin, ["*"], ["*"], AccountRules.Wildcard(_actor, superAdmin.Id), other);

        dto.RemovablePermissions.Should().BeEquivalentTo(offered ? ["*"] : Array.Empty<string>());
    }

    /// <summary>
    /// REVIEW 102 F3: a holder of * through a second platform role remains, so the
    /// wildcard is offered. Deliberate break: count every role grant as taken → red.
    /// </summary>
    [Fact]
    public async Task Handle_WithAHolderThroughASecondPlatformRole_OffersTheWildcard()
    {
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);

        var dto = await ReadAsync(
            superAdmin, ["*"], ["*"],
            AccountRules.Wildcard(_actor, superAdmin.Id), AccountRules.Wildcard(Guid.NewGuid(), Guid.NewGuid()));

        dto.RemovablePermissions.Should().BeEquivalentTo("*");
    }

    /// <summary>
    /// REVIEW 102 F5 (e): an actor who does not hold * cannot remove it whatever the
    /// count, so the full administrators are not read. Deliberate break: drop the
    /// actor's check from the short-circuit → red.
    /// </summary>
    [Fact]
    public async Task Handle_ForAnActorWithoutTheWildcard_ReadsNoFullAdministrators()
    {
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", isSystem: true);

        var dto = await ReadAsync(superAdmin, ["*"], AdministratorSet, AccountRules.Wildcard(Guid.NewGuid(), superAdmin.Id));

        dto.RemovablePermissions.Should().BeEmpty();
        _permissionRepositoryMock.Verify(
            p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// The full administrators are read only for the one removal the count can refuse:
    /// <c>*</c> on a platform role (bottleneck B2). Never per code.
    /// </summary>
    [Theory]
    [InlineData(false, "users:*")]
    [InlineData(true, "*")]
    public async Task Handle_WithoutWildcardOnAPlatformRole_ReadsNoFullAdministrators(bool applicationRole, string code)
    {
        var role = TestHelpers.CreateRole(applicationId: applicationRole ? Guid.NewGuid() : null);

        var dto = await ReadAsync(role, [code], ["*"]);

        dto.RemovablePermissions.Should().BeEquivalentTo(code);
        _permissionRepositoryMock.Verify(
            p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()), Times.Never());
        _permissionRepositoryMock.Verify(
            p => p.GetUserEffectivePermissionsAsync(_actor, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_WithoutTheCaller_ComputesNoRemovablePermissions()
    {
        var role = TestHelpers.CreateRole();
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(code: "*")]);

        var result = await _handler.Handle(new GetRoleByIdQuery(role.Id), CancellationToken.None);

        result.Value.RemovablePermissions.Should().BeNull("absent, which a client reads as every code removable");
        _permissionRepositoryMock.Verify(
            p => p.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
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

        _applicationRepositoryMock
            .Setup(r => r.GetByIdIncludingDeletedAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, name: "CRM App"));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(roleId);
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.ApplicationName.Should().Be("CRM App");
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
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly GetRolesQueryHandler _handler;

    public GetRolesQueryHandlerTests()
    {
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();

        // Owners exist and are live unless a test says otherwise (OI-225 C4).
        _applicationRepositoryMock
            .Setup(r => r.GetByIdsIncludingDeletedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Select(id => TestHelpers.CreateApplication(id: id)).ToList());

        _handler = new GetRolesQueryHandler(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _userRepositoryMock.Object);
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
            .Setup(r => r.GetByApplicationAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission>());

        _applicationRepositoryMock
            .Setup(r => r.GetByIdsIncludingDeletedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [TestHelpers.CreateApplication(id: applicationId, name: "CRM App")]);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value[0].Code.Should().Be("ADMIN");
        result.Value[1].Code.Should().Be("VIEWER");
        result.Value.Should().OnlyContain(r => r.ApplicationName == "CRM App");

        // Distinct application ids resolve once, not per role.
        _applicationRepositoryMock.Verify(
            r => r.GetByIdsIncludingDeletedAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { applicationId })), It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetByApplicationAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
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
            .Setup(r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
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
            r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.GetByApplicationAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── OI-223 T7: which roles the caller may assign (canGrant) ──

    /// <summary>The seeded Administrator role's codes: every administration code but <c>*</c>.</summary>
    private static readonly string[] AdministratorSet = ["users:*", "roles:*", "permissions:*", "applications:*"];

    private async Task<IReadOnlyList<RoleDto>> ListAsync(Guid? actorId, string[] actorHolds, params (Role Role, string[] Codes)[] roles)
    {
        _roleRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles.Select(r => r.Role).ToList());
        foreach (var (role, codes) in roles)
        {
            _permissionRepositoryMock
                .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(codes.Select(code => TestHelpers.CreatePermission(code: code)).ToList());
        }

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(actorHolds);

        var result = await _handler.Handle(new GetRolesQuery { ActorId = actorId }, CancellationToken.None);
        result.IsError.Should().BeFalse();
        return result.Value;
    }

    /// <remarks>
    /// Deliberate break: read the caller's codes once per role (the count below goes red).
    /// </remarks>
    [Fact]
    public async Task Handle_ForTheSeededAdministratorSet_MarksSuperAdministratorNotGrantable_InOneActorRead()
    {
        var actorId = Guid.NewGuid();
        var superAdmin = TestHelpers.CreateRole(code: "super-admin", name: "Super Administrator");
        var userManager = TestHelpers.CreateRole(code: "user-manager", name: "User Manager");
        var support = TestHelpers.CreateRole(code: "support", name: "Support");

        var roles = await ListAsync(actorId, AdministratorSet,
            (superAdmin, ["*"]), (userManager, ["users:*", "roles:read"]), (support, ["users:read"]));

        roles.Single(r => r.Code == "super-admin").CanGrant.Should().BeFalse();
        roles.Single(r => r.Code == "user-manager").CanGrant.Should().BeTrue();
        roles.Single(r => r.Code == "support").CanGrant.Should().BeTrue();
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(actorId, It.IsAny<CancellationToken>()), Times.Once(),
            "what the caller may grant does not depend on the row, so it is read once per request");
    }

    /// <remarks>Deliberate break: drop the applications:update term.</remarks>
    [Theory]
    [InlineData(new[] { "crm:*" }, false)]
    [InlineData(new[] { "crm:*", "applications:update" }, true)]
    public async Task Handle_AnApplicationOwnedRole_IsGrantableOnlyWithApplicationsUpdate(string[] actorHolds, bool grantable)
    {
        var role = TestHelpers.CreateRole(applicationId: Guid.NewGuid(), code: "crm-agent", name: "Agent");

        var roles = await ListAsync(Guid.NewGuid(), actorHolds, (role, ["crm:leads:read"]));

        roles.Single().CanGrant.Should().Be(grantable);
    }


    /// <summary>The owners as the one read answers them: <paramref name="deleted"/> soft-deleted.</summary>
    private void OwnersRead(Guid live, Guid deleted)
    {
        var gone = TestHelpers.CreateApplication(id: deleted, name: "Retired App");
        gone.Delete(Guid.NewGuid());
        _applicationRepositoryMock
            .Setup(r => r.GetByIdsIncludingDeletedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateApplication(id: live, name: "Live App"), gone]);
    }

    /// <summary>
    /// OI-225 C4: assigning a role of a deleted application answers
    /// Application.NotFound, so the picker is told it cannot, even to a holder of *; the
    /// owners come from ONE read for the whole list. Deliberate breaks: ignore the
    /// deleted state; read the owners one by one.
    /// </summary>
    [Fact]
    public async Task Handle_ARoleOfADeletedApplication_IsNotGrantable_FromOneOwnerRead()
    {
        var (live, deleted) = (Guid.NewGuid(), Guid.NewGuid());
        OwnersRead(live, deleted);

        var roles = await ListAsync(Guid.NewGuid(), ["*"],
            (TestHelpers.CreateRole(applicationId: live, code: "live-agent"), ["live:read"]),
            (TestHelpers.CreateRole(applicationId: deleted, code: "retired-agent"), ["retired:read"]),
            (TestHelpers.CreateRole(applicationId: deleted, code: "retired-viewer"), ["retired:read"]),
            (TestHelpers.CreateRole(code: "auditor"), ["auditlogs:read"]));

        roles.Select(r => (r.Code, r.CanGrant, r.ApplicationName)).Should().BeEquivalentTo(new (string, bool?, string?)[]
        {
            ("live-agent", true, "Live App"),
            ("retired-agent", false, "Retired App"),
            ("retired-viewer", false, "Retired App"),
            ("auditor", true, null),
        });
        _applicationRepositoryMock.Verify(
            r => r.GetByIdsIncludingDeletedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once());
        _applicationRepositoryMock.Verify(
            r => r.GetByIdIncludingDeletedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_WithoutAnActor_ComputesNothing_AndReadsNoGrants()
    {
        var role = TestHelpers.CreateRole(code: "super-admin");

        var roles = await ListAsync(actorId: null, ["*"], (role, ["*"]));

        roles.Single().CanGrant.Should().BeNull("absent means not computed; a client treats it as grantable");
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
