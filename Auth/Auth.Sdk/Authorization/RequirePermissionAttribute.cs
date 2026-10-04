using Microsoft.AspNetCore.Authorization;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Attribute to require a specific permission for an endpoint.
/// Works with both JWT Bearer (checks "permissions" claim) and ApiKey (checks "permission" claims, one per key scope).
/// Never reads a token's OAuth "scope" claim, which is a grant, not a permission.
/// Supports wildcard permissions (e.g., "crm:*" matches "crm:leads:read").
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = $"{PolicyPrefix}{permission}";
    }

    public string Permission { get; }
}
