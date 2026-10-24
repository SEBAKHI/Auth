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
            .Setup(r => r.GetByIdIncludingDeletedAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, name: "CRM App"));

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
            r => r.GetByIdIncludingDeletedAsync(applicationId, It.IsAny<CancellationToken>()),
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
}
