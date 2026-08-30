using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Grants an <see cref="OrganizationPermissionRequirement"/> only when the caller
/// holds the permission <em>in the organization the request names</em>.
/// </summary>
/// <remarks>
/// Reads the <c>org_perm</c> claims, each of the form
/// <c>{organizationId}:{permissionCode}</c>, and matches only the ones whose
/// organization equals the one resolved from the route. Wildcards are honoured
/// within that organization and nowhere else.
/// <para>
/// Two things deliberately do NOT satisfy this requirement:
/// </para>
/// <list type="bullet">
/// <item>
/// A matching code in the flat <c>permissions</c> claim. That claim carries
/// application-wide authority; treating it as satisfying an organization-scoped
/// requirement is precisely the conflation this handler exists to prevent.
/// </item>
/// <item>
/// A request whose route names no organization. An unresolvable scope is not an
/// absent one - it means the endpoint is mis-annotated, and failing closed makes
/// that visible on the first call rather than after an incident.
/// </item>
/// </list>
/// </remarks>
public class OrganizationPermissionRequirementHandler
    : AuthorizationHandler<OrganizationPermissionRequirement>
{
    private const string OrganizationPermissionsClaimType = "org_perm";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OrganizationPermissionRequirementHandler> _logger;

    public OrganizationPermissionRequirementHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<OrganizationPermissionRequirementHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning(
                "No HttpContext available; cannot resolve the organization for {Permission}",
                requirement.Permission);
            return Task.CompletedTask;
        }

        if (ResolveOrganizationId(httpContext, requirement) is not Guid organizationId)
        {
            _logger.LogWarning(
                "Denied {Permission}: the route names no organization. Add an orgId (or organizationId) " +
                "route parameter, or pass the parameter name to [RequireOrganizationPermission].",
                requirement.Permission);
            return Task.CompletedTask;
        }

        var held = context.User.FindAll(OrganizationPermissionsClaimType)
            .Select(claim => Split(claim.Value))
            .Where(parsed => parsed?.OrganizationId == organizationId)
            .Select(parsed => parsed!.Value.Code)
            .ToList();

        if (Matches(held, requirement.Permission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Denied {Permission} in organization {OrganizationId}. Held there: [{Held}]",
            requirement.Permission, organizationId, string.Join(", ", held));

        return Task.CompletedTask;
    }

    private static Guid? ResolveOrganizationId(
        HttpContext httpContext,
        OrganizationPermissionRequirement requirement)
    {
        var names = requirement.RouteParameterName is { Length: > 0 } configured
            ? [configured]
            : OrganizationPermissionRequirement.DefaultRouteParameterNames;

        foreach (var name in names)
        {
            if (httpContext.Request.RouteValues.TryGetValue(name, out var value)
                && Guid.TryParse(value?.ToString(), out var organizationId))
            {
                return organizationId;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an <c>org_perm</c> value. Permission codes contain ':' themselves,
    /// so only the first separator - the one straight after the GUID, which never
    /// contains one - is significant.
    /// </summary>
    private static (Guid OrganizationId, string Code)? Split(string value)
    {
        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return null;
        }

        return Guid.TryParse(value[..separator], out var organizationId)
            ? (organizationId, value[(separator + 1)..])
            : null;
    }

    /// <summary>
    /// Wildcard matching, identical in shape to the flat handler's - but applied
    /// only to codes already narrowed to the target organization, so a '*' held in
    /// one organization grants nothing in another.
    /// </summary>
    private static bool Matches(IEnumerable<string> held, string required)
    {
        foreach (var permission in held)
        {
            if (permission == "*")
            {
                return true;
            }

            if (string.Equals(permission, required, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (permission.EndsWith(":*", StringComparison.Ordinal))
            {
                var prefix = permission[..^2];
                if (required.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(required, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
