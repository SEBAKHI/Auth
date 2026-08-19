using Auth.Application.Common;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// The organization-scoped half of "no principal may grant what it does not
/// hold".
/// </summary>
/// <remarks>
/// Two organization paths handed authority over without asking the question at
/// all: granting a permission to a member, and assigning an application role to
/// one. The endpoint gate asks only whether the actor may grant — which the
/// seeded <c>org-admin</c> role satisfies through <c>org:permissions:*</c> — so
/// an organization administrator could hand any member, itself included, every
/// permission of every application the organization has enabled, regardless of
/// what it holds there.
///
/// Both handlers also shipped with no unit tests of their own, which is how a
/// missing guard stayed invisible.
/// </remarks>
public class OrganizationGrantGuardTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Application = Guid.NewGuid();

    private static OrganizationGrantGuard GuardHolding(
        string[] withinOrganization,
        string[] acrossPlatform)
    {
        var organizations = new Mock<IOrganizationRepository>();
        organizations
            .Setup(r => r.GetEffectivePermissionCodesAsync(
                Organization, Actor, Application, It.IsAny<CancellationToken>()))
            .ReturnsAsync(withinOrganization);

        var permissions = new Mock<IPermissionRepository>();
        permissions
            .Setup(r => r.GetUserEffectivePermissionsAsync(Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acrossPlatform);

        return new OrganizationGrantGuard(organizations.Object, permissions.Object);
    }

    private static Task<ErrorOr.ErrorOr<ErrorOr.Success>> Grant(
        OrganizationGrantGuard guard, params string[] requested) =>
        guard.EnsureCanGrantAsync(
            Organization, Actor, Application, requested, CancellationToken.None);

    [Fact]
    public async Task OrgAdmin_MayNotGrantWhatItDoesNotHoldInThatApplication()
    {
        // The defect, stated as a test: org:permissions:manage is authority to
        // administer grants, not authority over every code the application
        // defines.
        var guard = GuardHolding(
            withinOrganization: ["org:permissions:manage", "crm:leads:read"],
            acrossPlatform: []);

        var result = await Grant(guard, "crm:admin");

        result.IsError.Should().BeTrue(
            "an organization administrator may delegate only what it holds in that application");
    }

    [Fact]
    public async Task OrgAdmin_MayGrantWhatItHoldsInThatApplication()
    {
        var guard = GuardHolding(
            withinOrganization: ["crm:leads:read", "crm:leads:write"],
            acrossPlatform: []);

        var result = await Grant(guard, "crm:leads:read");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task PrefixWildcardHeldInTheOrganization_CoversItsArea()
    {
        var guard = GuardHolding(
            withinOrganization: ["crm:*"],
            acrossPlatform: []);

        var result = await Grant(guard, "crm:leads:read", "crm:leads:write");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task PrefixWildcard_DoesNotReachAnotherArea()
    {
        var guard = GuardHolding(
            withinOrganization: ["crm:*"],
            acrossPlatform: []);

        var result = await Grant(guard, "billing:read");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task PlatformSuperAdmin_PassesThroughTheGlobalWildcard()
    {
        // Load-bearing. A platform operator administering an organization holds
        // nothing scoped to it, so an organization-only check would lock them
        // out of a surface they are meant to administer. The union with the
        // platform set answers this without a bypass flag.
        var guard = GuardHolding(
            withinOrganization: [],
            acrossPlatform: ["*"]);

        var result = await Grant(guard, "crm:admin", "billing:write");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task NarrowPlatformAuthority_DoesNotBecomeUnlimited()
    {
        // The reason the platform set is unioned in rather than treated as a
        // bypass: holding organizations:manage is authority to administer
        // organizations, not authority over every application permission a
        // tenant has enabled.
        var guard = GuardHolding(
            withinOrganization: [],
            acrossPlatform: ["organizations:manage", "users:read"]);

        var result = await Grant(guard, "crm:admin");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task NothingRequested_IsAllowed()
    {
        // A role that carries no permissions hands over no authority, so there
        // is nothing to refuse — and no repository call worth making.
        var guard = GuardHolding(withinOrganization: [], acrossPlatform: []);

        var result = await Grant(guard);

        result.IsError.Should().BeFalse();
    }
}
