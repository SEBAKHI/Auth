using Microsoft.AspNetCore.Authorization;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Requirement for a specific permission.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
