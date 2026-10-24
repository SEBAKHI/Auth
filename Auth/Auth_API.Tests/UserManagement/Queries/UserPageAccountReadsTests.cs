using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Users.GetUserOrganizationRoles;
using Auth.Application.Features.Users.GetUserPermissions;
using Auth.Application.Features.Users.GetUserRoles;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Queries;

/// <summary>
/// OI-225 (2), (3) and (11): what the user page reads about an account. A row scoped to
/// a deleted application names it and says it is deleted, instead of reading like a
/// platform-wide assignment; the roles held through organizations are read for the page;
/// and the reset of the second factor is offered by the reset's own rules.
/// </summary>
public class UserPageAccountReadsTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Application _deleted = TestHelpers.CreateApplication(name: "Retired App", code: "retired");

    public UserPageAccountReadsTests()
    {
        _deleted.Delete(Guid.NewGuid());
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: _userId));
        _applications.Setup(r => r.GetByIdIncludingDeletedAsync(_deleted.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_deleted);
    }

    /// <remarks>Deliberate break: resolve the application with GetByIdAsync again.</remarks>
    [Fact]
    public async Task ARoleOfADeletedApplication_IsNamed_AndFlaggedDeleted()
    {
        var role = TestHelpers.CreateRole(applicationId: _deleted.Id, name: "Agent");
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _users.Setup(r => r.GetUserRolesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateUserRole(userId: _userId, roleId: role.Id, applicationId: _deleted.Id)]);

        var result = await new GetUserRolesQueryHandler(
                _users.Object, _roles.Object, _applications.Object, AccountRules.Policy(), Mock.Of<ILogger<GetUserRolesQueryHandler>>())
            .Handle(new GetUserRolesQuery(_userId), CancellationToken.None);

        var row = result.Value.Should().ContainSingle().Subject;
        row.ApplicationName.Should().Be("Retired App");
        row.ApplicationIsDeleted.Should().BeTrue();
    }

    /// <remarks>Deliberate break: resolve the application with GetByIdAsync again.</remarks>
    [Fact]
    public async Task APermissionOfADeletedApplication_IsNamed_AndFlaggedDeleted()
    {
        var permission = TestHelpers.CreatePermission(applicationId: _deleted.Id, code: "retired:read");
        _permissions.Setup(r => r.GetByIdAsync(permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(permission);
        _users.Setup(r => r.GetUserPermissionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateUserPermission(userId: _userId, permissionId: permission.Id, applicationId: _deleted.Id)]);

        var result = await new GetUserPermissionsQueryHandler(
                _users.Object, _permissions.Object, _applications.Object, AccountRules.Policy(), Mock.Of<ILogger<GetUserPermissionsQueryHandler>>())
            .Handle(new GetUserPermissionsQuery(_userId), CancellationToken.None);

        var row = result.Value.Should().ContainSingle().Subject;
        row.ApplicationName.Should().Be("Retired App");
        row.ApplicationIsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task APlatformRow_CarriesNoDeletedFlag()
    {
        var role = TestHelpers.CreateRole(applicationId: null);
        _roles.Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _users.Setup(r => r.GetUserRolesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateUserRole(userId: _userId, roleId: role.Id)]);

        var result = await new GetUserRolesQueryHandler(
                _users.Object, _roles.Object, _applications.Object, AccountRules.Policy(), Mock.Of<ILogger<GetUserRolesQueryHandler>>())
            .Handle(new GetUserRolesQuery(_userId), CancellationToken.None);

        result.Value.Single().ApplicationIsDeleted.Should().BeNull();
    }

    [Fact]
    public async Task TheRolesHeldThroughOrganizations_AreReadInOneCall_AndMapped()
    {
        var row = new UserOrganizationRoleRow
        {
            OrganizationId = Guid.NewGuid(), OrganizationCode = "org-a", OrganizationName = "Organization A",
            ApplicationId = _deleted.Id, ApplicationCode = "retired", ApplicationName = "Retired App", ApplicationIsDeleted = true,
            RoleId = Guid.NewGuid(), RoleCode = "agent", RoleName = "Agent", ExpiresAt = DateTime.UtcNow.AddDays(3),
        };
        _organizations.Setup(r => r.GetUserApplicationRolesAcrossOrganizationsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);

        var result = await new GetUserOrganizationRolesQueryHandler(_users.Object, _organizations.Object)
            .Handle(new GetUserOrganizationRolesQuery(_userId), CancellationToken.None);

        result.Value.Should().ContainSingle().Which.Should().BeEquivalentTo(row);
        _organizations.Verify(
            r => r.GetUserApplicationRolesAcrossOrganizationsAsync(_userId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task TheRolesHeldThroughOrganizations_OfAnUnknownUser_AreNotFound()
    {
        var result = await new GetUserOrganizationRolesQueryHandler(_users.Object, _organizations.Object)
            .Handle(new GetUserOrganizationRolesQuery(Guid.NewGuid()), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.NotFound");
        _organizations.Invocations.Should().BeEmpty();
    }

    /// <summary>
    /// The reset's capability (11): never one's own account, never the system account,
    /// never an account holding platform codes the actor lacks; the last-full-administrator
    /// rule never applies, since a reset takes no grant away. Deliberate breaks: drop the
    /// reset's own first rule; drop rule 2.
    /// </summary>
    [Theory]
    [InlineData("self", false)]
    [InlineData("system", false)]
    [InlineData("higher", false)]
    [InlineData("covered", true)]
    [InlineData("lastFullAdministrator", true)]
    public async Task TheResetCapability_FollowsTheResetsRules(string target, bool offered)
    {
        var actorId = Guid.NewGuid();
        var accountId = target switch
        {
            "self" => actorId,
            "system" => WellKnownUserIds.System,
            _ => Guid.NewGuid(),
        };
        var permissions = new Mock<IPermissionRepository>();
        var policy = AccountRules.Policy(permissions);
        permissions.Setup(p => p.GetUserEffectivePermissionsAsync(actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target == "lastFullAdministrator" ? ["*"] : ["users:*"]);
        if (accountId != actorId)
        {
            permissions.Setup(p => p.GetUserEffectivePermissionsAsync(accountId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target switch
                {
                    "higher" => ["users:read", "roles:update"],
                    "covered" => ["users:read"],
                    "lastFullAdministrator" => ["*"],
                    _ => (IReadOnlyList<string>)[],
                });
        }

        if (target == "lastFullAdministrator")
        {
            // The account is the only full administrator: rule 3 refuses a lock of it.
            permissions.Setup(p => p.GetUsersHoldingWildcardPermissionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([AccountRules.Wildcard(accountId, Guid.NewGuid())]);
        }

        var decisions = await policy.DecideForAsync(actorId, [accountId], CancellationToken.None);

        UserAllowedActionsDto.For(decisions, accountId).ResetTwoFactor.Should().Be(offered);
        if (target == "lastFullAdministrator")
        {
            decisions.Allows(accountId, AccountAction.Lock).Should().BeFalse("the fixture is the last full administrator");
        }
    }

    /// <summary>The handler and the capability share rule 1: one definition on the account.</summary>
    [Fact]
    public void TheResetsFirstRule_IsOneDefinition()
    {
        var actorId = Guid.NewGuid();
        var account = TestHelpers.CreateUser();

        User.IsTwoFactorResettableBy(account.Id, actorId).Should().BeTrue();
        User.IsTwoFactorResettableBy(actorId, actorId).Should().BeFalse();
        User.IsTwoFactorResettableBy(WellKnownUserIds.System, actorId).Should().BeFalse();
        account.EnsureTwoFactorResettableBy(account.Id).IsError.Should().BeTrue();
        account.EnsureTwoFactorResettableBy(actorId).IsError.Should().BeFalse();
    }
}
