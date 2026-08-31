using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Handler that checks if the authenticated principal has the required
/// APPLICATION-WIDE permission.
/// Checks multiple claim types to support both JWT Bearer and ApiKey authentication:
/// - "permissions" (from JWT Bearer tokens)
/// - "permission" (from ApiKey authentication handler)
/// - "scope" (from ApiKey authentication handler)
/// Supports wildcard matching (e.g., "crm:*" matches "crm:leads:read").
/// </summary>
/// <remarks>
/// These claims carry authority that is not tied to any organization. A
/// permission delegated to the caller INSIDE an organization never appears here -
/// it arrives as an <c>org_perm</c> claim tagged with the organization that
/// granted it, and is checked by
/// <see cref="OrganizationPermissionRequirementHandler"/>.
/// <para>
/// The separation is the fix for a real defect: application tokens used to
/// flatten every organization's delegated permissions into this one claim, so a
/// user who belonged to two organizations that both enabled the application got a
/// single unscoped list, and a permission granted in one was spendable on the
/// other's data. If your endpoint acts on one organization's records, use
/// <see cref="RequireOrganizationPermissionAttribute"/>, not
/// <see cref="RequirePermissionAttribute"/>.
/// </para>
/// </remarks>
public class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private static readonly string[] PermissionClaimTypes = ["permissions", "permission", "scope"];
    private readonly ILogger<PermissionRequirementHandler> _logger;

    public PermissionRequirementHandler(ILogger<PermissionRequirementHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var userPermissions = PermissionClaimTypes
            .SelectMany(claimType => context.User.FindAll(claimType))
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        if (PermissionMatches(userPermissions, requirement.Permission))
        {
            _logger.LogDebug("Permission {Permission} granted", requirement.Permission);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "Permission denied - missing {Permission}. Available: [{UserPermissions}]",
                requirement.Permission, string.Join(", ", userPermissions));
        }

        return Task.CompletedTask;
    }

    private static bool PermissionMatches(IEnumerable<string> userPermissions, string requiredPermission)
    {
        foreach (var heldPermission in userPermissions)
        {
            if (heldPermission == "*")
                return true;

            if (string.Equals(heldPermission, requiredPermission, StringComparison.OrdinalIgnoreCase))
                return true;

            if (heldPermission.EndsWith(":*"))
            {
                var prefix = heldPermission[..^2];
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
