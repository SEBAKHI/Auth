using System.Security.Claims;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authorization;

public class PermissionRequirementHandlerTests
{
    private readonly Mock<ILogger<PermissionRequirementHandler>> _loggerMock = new();
    private readonly PermissionRequirementHandler _handler;

    public PermissionRequirementHandlerTests()
    {
        _handler = new PermissionRequirementHandler(_loggerMock.Object);
    }

    private static AuthorizationHandlerContext CreateContext(
        PermissionRequirement requirement,
        ClaimsPrincipal? user = null)
    {
        user ??= new ClaimsPrincipal(new ClaimsIdentity());
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            resource: null);
    }

    [Fact]
    public async Task HandleRequirementAsync_NoUserIdClaim_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:read");
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_InvalidUserIdClaim_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:read");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, "not-a-guid")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_ExactPermissionMatch_Succeeds()
    {
        var requirement = new PermissionRequirement("users:read");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "users:read"),
            new Claim(JwtClaimNames.Permissions, "users:create")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoMatchingPermission_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:delete");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "users:read")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_GlobalWildcard_Succeeds()
    {
        var requirement = new PermissionRequirement("anything:here");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "*")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_PrefixWildcard_MatchesChildren()
    {
        var requirement = new PermissionRequirement("crm:leads:read");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "crm:*")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_PrefixWildcard_MatchesBase()
    {
        var requirement = new PermissionRequirement("crm");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "crm:*")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_PrefixWildcard_DoesNotMatchSiblings()
    {
        var requirement = new PermissionRequirement("sales:leads:read");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "crm:*")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_CaseInsensitiveMatch_Succeeds()
    {
        var requirement = new PermissionRequirement("USERS:READ");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Permissions, "users:read")
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoPermissionClaims_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:read");
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString())
        }, "test");
        var context = CreateContext(requirement, new ClaimsPrincipal(identity));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // ----- Organization-scoped ("org:*") requirements -----

    private static AuthorizationHandlerContext CreateOrgContext(
        PermissionRequirement requirement,
        ClaimsPrincipal user,
        Guid routeOrganizationId,
        IOrganizationRepository organizationRepository)
    {
        var services = new ServiceCollection()
            .AddSingleton(organizationRepository)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.RouteValues["id"] = routeOrganizationId.ToString();

        return new AuthorizationHandlerContext(new[] { requirement }, user, httpContext);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task HandleRequirementAsync_OrgScopedClaim_MatchingOrganization_Succeeds()
    {
        var orgId = Guid.NewGuid();
        var requirement = new PermissionRequirement("org:members:read");
        var user = CreatePrincipal(
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.OrgPermissions, $"{orgId}:org:*"));
        var repositoryMock = new Mock<IOrganizationRepository>();
        var context = CreateOrgContext(requirement, user, orgId, repositoryMock.Object);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        repositoryMock.Verify(
            r => r.GetMembershipPermissionCodesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleRequirementAsync_OrgScopedClaim_OtherOrganization_DoesNotSucceed()
    {
        var memberOrgId = Guid.NewGuid();
        var targetOrgId = Guid.NewGuid();
        var requirement = new PermissionRequirement("org:members:read");
        var user = CreatePrincipal(
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.OrgPermissions, $"{memberOrgId}:org:*"));
        var repositoryMock = new Mock<IOrganizationRepository>();
        repositoryMock
            .Setup(r => r.GetMembershipPermissionCodesAsync(
                targetOrgId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var context = CreateOrgContext(requirement, user, targetOrgId, repositoryMock.Object);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoOrgClaims_LiveMembershipGrants_Succeeds()
    {
        // A token issued before the membership existed (e.g. the org was just
        // created) has no org_perm claims — the gate checks the live role.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requirement = new PermissionRequirement("org:members:read");
        var user = CreatePrincipal(new Claim(JwtClaimNames.Subject, userId.ToString()));
        var repositoryMock = new Mock<IOrganizationRepository>();
        repositoryMock
            .Setup(r => r.GetMembershipPermissionCodesAsync(
                orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "org:*" });
        var context = CreateOrgContext(requirement, user, orgId, repositoryMock.Object);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoOrgClaims_NonMember_DoesNotSucceed()
    {
        var orgId = Guid.NewGuid();
        var requirement = new PermissionRequirement("org:members:manage");
        var user = CreatePrincipal(new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()));
        var repositoryMock = new Mock<IOrganizationRepository>();
        repositoryMock
            .Setup(r => r.GetMembershipPermissionCodesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var context = CreateOrgContext(requirement, user, orgId, repositoryMock.Object);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_NonOrgPermission_DoesNotUseOrgFallback()
    {
        var orgId = Guid.NewGuid();
        var requirement = new PermissionRequirement("users:read");
        var user = CreatePrincipal(
            new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.OrgPermissions, $"{orgId}:org:*"));
        var repositoryMock = new Mock<IOrganizationRepository>();
        var context = CreateOrgContext(requirement, user, orgId, repositoryMock.Object);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        repositoryMock.Verify(
            r => r.GetMembershipPermissionCodesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

public class PermissionPolicyProviderTests
{
    private readonly PermissionPolicyProvider _provider;

    public PermissionPolicyProviderTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        _provider = new PermissionPolicyProvider(options);
    }

    [Fact]
    public async Task GetPolicyAsync_PermissionPolicy_ReturnsPolicy()
    {
        var policy = await _provider.GetPolicyAsync("Permission:users:read");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().HaveCount(2);
        policy.Requirements.Should().ContainSingle(r => r is PermissionRequirement);
        policy.Requirements.Should().ContainSingle(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task GetPolicyAsync_NonPermissionPolicy_DelegatesToFallback()
    {
        var policy = await _provider.GetPolicyAsync("SomeOtherPolicy");

        // Default fallback returns null for unknown policies
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_ReturnsPolicy()
    {
        var policy = await _provider.GetDefaultPolicyAsync();

        policy.Should().NotBeNull();
    }
}
