using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace Auth_API.Authorization;

/// <summary>
/// Handler that checks if user has the required permission.
/// Checks JWT claims first for efficiency, avoiding database calls.
/// For organization-scoped requirements ("org:*"), the target organization id
/// is resolved from the route and matched against the token's per-organization
/// membership claims, with a live membership lookup as fallback for tokens
/// issued before the membership existed (e.g. an organization created within
/// the current session).
/// </summary>
public class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionRequirementHandler> _logger;

    public PermissionRequirementHandler(ILogger<PermissionRequirementHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(JwtClaimNames.Subject)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogDebug("No valid user ID claim found for permission check");
            return; // Not authenticated
        }

        // Get permissions from JWT claims (they're already embedded in the token)
        var userPermissions = context.User.FindAll(JwtClaimNames.Permissions)
            .Select(c => c.Value)
            .ToList();

        if (PermissionMatches(userPermissions, requirement.Permission))
        {
            _logger.LogDebug(
                "User {UserId} has permission {Permission}",
                userId, requirement.Permission);
            context.Succeed(requirement);
            return;
        }

        // Organization-scoped fallback: org endpoints carry the target org id
        // in the route; membership-role permissions ride in "org_perm" claims.
        if (requirement.Permission.StartsWith("org:", StringComparison.OrdinalIgnoreCase) &&
            context.Resource is HttpContext httpContext &&
            ResolveOrganizationRouteId(httpContext) is Guid organizationId)
        {
            var organizationPermissions = context.User.FindAll(JwtClaimNames.OrgPermissions)
                .Select(claim => SplitOrgPermission(claim.Value))
                .Where(parsed => parsed?.OrganizationId == organizationId)
                .Select(parsed => parsed!.Value.Code)
                .ToList();

            if (PermissionMatches(organizationPermissions, requirement.Permission))
            {
                _logger.LogDebug(
                    "User {UserId} has org-scoped permission {Permission} for organization {OrganizationId}",
                    userId, requirement.Permission, organizationId);
                context.Succeed(requirement);
                return;
            }

            if (organizationPermissions.Count == 0)
            {
                // The token predates the membership (e.g. the organization was
                // created this session) — check the live membership role once.
                var organizationRepository = httpContext.RequestServices
                    .GetService<IOrganizationRepository>();
                if (organizationRepository is not null)
                {
                    var liveCodes = await organizationRepository.GetMembershipPermissionCodesAsync(
                        organizationId, userId, httpContext.RequestAborted);

                    if (PermissionMatches(liveCodes, requirement.Permission))
                    {
                        _logger.LogDebug(
                            "User {UserId} granted {Permission} for organization {OrganizationId} via live membership lookup",
                            userId, requirement.Permission, organizationId);
                        context.Succeed(requirement);
                        return;
                    }
                }
            }
        }

        _logger.LogWarning(
            "User {UserId} denied access - missing permission {Permission}. User permissions: [{UserPermissions}]",
            userId, requirement.Permission, string.Join(", ", userPermissions));
    }

    /// <summary>
    /// Resolves the target organization id from the route. Organization
    /// endpoints use either "{orgId:guid}" (member sub-resources) or
    /// "{id:guid}" (the organization itself).
    /// </summary>
    private static Guid? ResolveOrganizationRouteId(HttpContext httpContext)
    {
        var routeValue = httpContext.Request.RouteValues.TryGetValue("orgId", out var orgIdValue)
            ? orgIdValue
            : httpContext.Request.RouteValues.GetValueOrDefault("id");

        return Guid.TryParse(routeValue?.ToString(), out var organizationId)
            ? organizationId
            : null;
    }

    /// <summary>
    /// Parses an "org_perm" claim value ("{organizationId}:{permissionCode}").
    /// Permission codes contain ':' themselves, so only the first separator
    /// (right after the GUID, which never contains one) is significant.
    /// </summary>
    private static (Guid OrganizationId, string Code)? SplitOrgPermission(string value)
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
    /// Checks if the user's permissions match the required permission using wildcard logic.
    /// Public so controllers can evaluate optional scope-widening claims (e.g.
    /// platform administration) with identical semantics to endpoint gating.
    /// </summary>
    public static bool PermissionMatches(IEnumerable<string> userPermissions, string requiredPermission)
    {
        foreach (var heldPermission in userPermissions)
        {
            // Global wildcard grants everything
            if (heldPermission == "*")
                return true;

            // Exact match
            if (string.Equals(heldPermission, requiredPermission, StringComparison.OrdinalIgnoreCase))
                return true;

            // Wildcard matching (e.g., "crm:*" matches "crm:leads:read")
            if (heldPermission.EndsWith(":*"))
            {
                var prefix = heldPermission[..^2]; // Remove ":*"
                if (requiredPermission.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(requiredPermission, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
