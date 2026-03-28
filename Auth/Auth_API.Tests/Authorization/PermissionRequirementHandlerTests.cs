using System.Security.Claims;
using Auth.Domain.Constants;
using Auth_API.Authorization;
using Microsoft.AspNetCore.Authorization;
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
