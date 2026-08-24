using Auth.Application.Common;
using Auth.Application.Features.Users.AssignRole;
using Auth.Application.Features.Users.GrantUserPermission;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// No principal may grant a permission it does not itself hold.
/// </summary>
/// <remarks>
/// The endpoint attributes answer "may this actor grant at all". Until this
/// guard there was no answer to "may it grant THIS", so a holder of
/// <c>users:manage-permissions</c> could hand itself the global <c>*</c> row,
/// and a holder of <c>users:manage-roles</c> could assign itself the
/// super-admin role. Both are one API call, and neither left the endpoint gate
/// unsatisfied.
///
/// Latent so far only because no platform permission code was ever seeded, so
/// no built-in role held either code. Seeding them makes it live, which is why
/// this ships first and separately.
/// </remarks>
public class PermissionGrantGuardTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static PermissionGrantGuard GuardHolding(
        Mock<IPermissionRepository> repository, params string[] held)
    {
        repository
            .Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(held);
        return new PermissionGrantGuard(repository.Object);
    }

    [Fact]
    public async Task GlobalWildcardHolder_MayGrantAnything()
    {
        // Load-bearing: if "*" ever stopped passing, nobody could grant
        // anything and the platform would be unadministrable with no way back
        // except direct SQL.
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "*");

        var result = await guard.EnsureCanGrantAsync(
            Actor, ["users:read", "secrets.manage", "*"], CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task PrefixWildcardHolder_MayGrantWithinItsArea()
    {
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "users:*");

        var result = await guard.EnsureCanGrantAsync(
            Actor, ["users:read", "users:manage-roles"], CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task PrefixWildcardHolder_MayNotReachOutsideItsArea()
    {
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "users:*");

        var result = await guard.EnsureCanGrantAsync(
            Actor, ["roles:read"], CancellationToken.None);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
    }

    [Fact]
    public async Task PermissionManager_CannotPromoteItselfToGlobalWildcard()
    {
        // The escalation this exists to stop, stated as its own case.
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "users:*", "permissions:*");

        var result = await guard.EnsureCanGrantAsync(Actor, ["*"], CancellationToken.None);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
    }

    [Fact]
    public async Task DotSeparatedCode_IsNotCoveredByAColonWildcard()
    {
        // secrets.manage uses a dot, so no ":*" wildcard can ever reach it and
        // it must always be held explicitly.
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "secrets:*");

        var result = await guard.EnsureCanGrantAsync(
            Actor, ["secrets.manage"], CancellationToken.None);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
    }

    [Fact]
    public async Task OneUnheldCodeAmongManyRefusesTheWholeRequest()
    {
        var guard = GuardHolding(new Mock<IPermissionRepository>(), "users:*");

        var result = await guard.EnsureCanGrantAsync(
            Actor, ["users:read", "system-settings:manage"], CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ActorPermissionsAreReadLive_NotFromTheToken()
    {
        // A token outlives a revocation. If the guard trusted its claims, an
        // actor stripped of a permission a minute ago could still re-issue it
        // to another account, where it would survive the revocation entirely.
        var repository = new Mock<IPermissionRepository>();
        var guard = GuardHolding(repository, "users:read");

        await guard.EnsureCanGrantAsync(Actor, ["users:read"], CancellationToken.None);

        repository.Verify(
            r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()),
            Times.Once());
    }
}

/// <summary>
/// The same rule seen through the two handlers that hand authority over.
/// </summary>
public class PermissionGrantGuardHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task GrantUserPermission_RefusesAPermissionTheActorLacks()
    {
        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var users = new Mock<IUserRepository>();
        var permissions = new Mock<IPermissionRepository>();

        users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));
        permissions.Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: permissionId, code: "secrets.manage"));
        permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["users:*"]);

        var handler = new GrantUserPermissionCommandHandler(
            users.Object,
            permissions.Object,
            new PermissionGrantGuard(permissions.Object),
            new Mock<IPublisher>().Object,
            new Mock<ILogger<GrantUserPermissionCommandHandler>>().Object);

        var result = await handler.Handle(
            new GrantUserPermissionCommand(userId, permissionId) { GrantedBy = Actor },
            CancellationToken.None);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        users.Verify(
            r => r.GrantPermissionAsync(It.IsAny<UserPermission>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task AssignRole_RefusesARoleCarryingMoreThanTheActorHolds()
    {
        // Assigning a role transfers everything it carries, so the super-admin
        // role was assignable by anyone who could assign roles at all.
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var users = new Mock<IUserRepository>();
        var roles = new Mock<IRoleRepository>();
        var applications = new Mock<IApplicationRepository>();
        var permissions = new Mock<IPermissionRepository>();

        users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));
        roles.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, name: "super-admin"));
        permissions.Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(code: "*")]);
        permissions.Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["users:*"]);

        var handler = new AssignRoleCommandHandler(
            users.Object,
            roles.Object,
            applications.Object,
            permissions.Object,
            new PermissionGrantGuard(permissions.Object),
            new Mock<IPublisher>().Object,
            new Mock<ILogger<AssignRoleCommandHandler>>().Object);

        var result = await handler.Handle(
            new AssignRoleCommand(userId, roleId) { AssignedBy = Actor },
            CancellationToken.None);

        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        roles.Verify(
            r => r.AssignToUserAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
