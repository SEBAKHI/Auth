using System.Security.Claims;
using Auth.Sdk.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// The enforcement point for organization-scoped authority in the SDK that
/// integrating applications actually ship.
///
/// <para>
/// The defect this exists to prevent: an application token used to flatten every
/// organization's delegated permissions into one unscoped <c>permissions</c>
/// claim. A user in organization A (granted <c>invoices:delete</c> there) and in
/// organization B received a single token carrying a bare
/// <c>invoices:delete</c>, and the SDK's flat handler granted it whatever
/// organization's data the request named. Permissions are now tagged with the
/// organization that granted them, and this handler is what makes the tag
/// load-bearing rather than decorative.
/// </para>
/// </summary>
public class OrganizationPermissionRequirementHandlerTests
{
    private const string OrgPermissionClaim = "org_perm";

    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    private static OrganizationPermissionRequirementHandler CreateHandler(HttpContext? httpContext)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        return new OrganizationPermissionRequirementHandler(
            accessor.Object,
            new Mock<ILogger<OrganizationPermissionRequirementHandler>>().Object);
    }

    private static ClaimsPrincipal Principal(params (Guid OrganizationId, string Code)[] organizationPermissions)
    {
        var claims = organizationPermissions
            .Select(p => new Claim(OrgPermissionClaim, $"{p.OrganizationId}:{p.Code}"))
            .ToList();

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static DefaultHttpContext ContextForOrganization(Guid? organizationId, string routeName = "orgId")
    {
        var context = new DefaultHttpContext();
        if (organizationId is Guid id)
        {
            context.Request.RouteValues[routeName] = id.ToString();
        }
        return context;
    }

    private static async Task<bool> EvaluateAsync(
        ClaimsPrincipal user,
        HttpContext? httpContext,
        string permission,
        string? routeParameterName = null)
    {
        var requirement = new OrganizationPermissionRequirement(permission, routeParameterName);
        var context = new AuthorizationHandlerContext([requirement], user, httpContext);

        await CreateHandler(httpContext).HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Grants_WhenThePermissionIsHeldInTheOrganizationTheRouteNames()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:delete")),
            ContextForOrganization(_orgA),
            "invoices:delete");

        granted.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_WhenThePermissionIsHeldInADIFFERENTOrganization()
    {
        // The whole point. Before the fix this same principal, carrying the same
        // authority, was granted access to organization B's data because the
        // permission had arrived unscoped.
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:delete")),
            ContextForOrganization(_orgB),
            "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_WhenTheCodeMatchesOnlyInTheFlatPermissionsClaim()
    {
        // Application-wide authority is a different question and lives in a
        // different claim. Honouring it here would restore the conflation.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("permissions", "invoices:delete")], authenticationType: "Test"));

        var granted = await EvaluateAsync(user, ContextForOrganization(_orgA), "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_WhenTheRouteNamesNoOrganization()
    {
        // An unresolvable scope is not an absent one: it means the endpoint is
        // mis-annotated, and failing closed surfaces that on the first call.
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:delete")),
            ContextForOrganization(organizationId: null),
            "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_WhenThePrincipalIsNotAuthenticated()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var granted = await EvaluateAsync(anonymous, ContextForOrganization(_orgA), "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_WhenThereIsNoHttpContextToResolveTheOrganizationFrom()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:delete")),
            httpContext: null,
            "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Grants_ThroughAPrefixWildcardHeldInTheSameOrganization()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:*")),
            ContextForOrganization(_orgA),
            "invoices:delete");

        granted.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_AWildcardHeldInADifferentOrganization()
    {
        // A wildcard is the most dangerous claim to leak across a boundary, so it
        // gets its own case rather than riding on the exact-match one.
        var granted = await EvaluateAsync(
            Principal((_orgA, "*")),
            ContextForOrganization(_orgB),
            "invoices:delete");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Grants_ThroughTheGlobalWildcardHeldInTheSameOrganization()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "*")),
            ContextForOrganization(_orgA),
            "invoices:delete");

        granted.Should().BeTrue();
    }

    [Theory]
    [InlineData("orgId")]
    [InlineData("organizationId")]
    public async Task Resolves_TheConventionalRouteParameterNames(string routeName)
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:read")),
            ContextForOrganization(_orgA, routeName),
            "invoices:read");

        granted.Should().BeTrue();
    }

    [Fact]
    public async Task Resolves_AnExplicitlyNamedRouteParameter()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:read")),
            ContextForOrganization(_orgA, "tenant"),
            "invoices:read",
            routeParameterName: "tenant");

        granted.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_WhenTheExplicitRouteParameterIsAbsent()
    {
        var granted = await EvaluateAsync(
            Principal((_orgA, "invoices:read")),
            ContextForOrganization(_orgA, "orgId"),
            "invoices:read",
            routeParameterName: "tenant");

        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Ignores_MalformedOrganizationPermissionClaims()
    {
        // Permission codes contain ':' themselves, so only the separator right
        // after the GUID is significant - and a value with no GUID at all must
        // not throw, or one bad claim denies every request the token makes.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(OrgPermissionClaim, "not-a-guid:invoices:delete"),
                new Claim(OrgPermissionClaim, ":"),
                new Claim(OrgPermissionClaim, $"{_orgA}:invoices:delete"),
            ],
            authenticationType: "Test"));

        var granted = await EvaluateAsync(user, ContextForOrganization(_orgA), "invoices:delete");

        granted.Should().BeTrue("the one well-formed claim still has to be honoured");
    }
}
