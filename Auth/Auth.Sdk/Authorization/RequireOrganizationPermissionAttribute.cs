using Microsoft.AspNetCore.Authorization;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Requires a permission the caller holds <em>in the organization this request is
/// about</em>, rather than anywhere at all.
/// </summary>
/// <remarks>
/// Use this, not <see cref="RequirePermissionAttribute"/>, on any endpoint that
/// acts on one organization's data.
/// <para>
/// <b>Why the distinction is not cosmetic.</b> A user who belongs to two
/// organizations that both enable your application signs in once and receives one
/// token. Permissions delegated to them inside an organization arrive as
/// <c>org_perm</c> claims of the form <c>{organizationId}:{code}</c>, because a
/// permission granted in organization A says nothing about what the same person
/// may do to organization B's records. <see cref="RequirePermissionAttribute"/>
/// reads the flat <c>permissions</c> claim, which by design carries only
/// application-wide authority; asking it about an organization-scoped permission
/// is asking the wrong claim, and the answer is silently "no" rather than
/// dangerously "yes".
/// </para>
/// <para>
/// The target organization is taken from the route. Name the parameter
/// <c>orgId</c> or <c>organizationId</c>, or pass <see cref="RouteParameterName"/>
/// if your route calls it something else. If no organization can be resolved from
/// the route, authorization FAILS - an unresolvable scope is not an absent one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpDelete("organizations/{orgId:guid}/invoices/{id:guid}")]
/// [RequireOrganizationPermission("invoices:delete")]
/// public Task&lt;IActionResult&gt; Delete(Guid orgId, Guid id) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireOrganizationPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "OrgPermission:";

    /// <summary>Separates the permission code from the route parameter name.</summary>
    public const char PolicySeparator = '@';

    /// <param name="permission">
    /// The permission code, as granted inside an organization (for example
    /// <c>invoices:delete</c>).
    /// </param>
    /// <param name="routeParameterName">
    /// Route parameter carrying the organization id. Defaults to the conventional
    /// names <c>orgId</c> then <c>organizationId</c>.
    /// </param>
    public RequireOrganizationPermissionAttribute(string permission, string? routeParameterName = null)
    {
        Permission = permission;
        RouteParameterName = routeParameterName;
        Policy = $"{PolicyPrefix}{permission}{PolicySeparator}{routeParameterName}";
    }

    public string Permission { get; }

    /// <summary>Route parameter holding the organization id, or null for the defaults.</summary>
    public string? RouteParameterName { get; }
}
