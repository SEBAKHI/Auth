using Microsoft.AspNetCore.Authorization;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Requires <see cref="Permission"/> within the organization named by the current
/// request's route.
/// </summary>
/// <param name="permission">The permission code as granted inside an organization.</param>
/// <param name="routeParameterName">
/// Route parameter carrying the organization id, or null to try the conventional
/// names in order.
/// </param>
public class OrganizationPermissionRequirement(string permission, string? routeParameterName)
    : IAuthorizationRequirement
{
    /// <summary>Route parameter names tried, in order, when none is specified.</summary>
    public static readonly string[] DefaultRouteParameterNames = ["orgId", "organizationId"];

    public string Permission { get; } = permission;

    public string? RouteParameterName { get; } = routeParameterName;
}
