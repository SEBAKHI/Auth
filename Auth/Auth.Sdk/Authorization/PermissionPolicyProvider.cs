using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Auth.Sdk.Authorization;

/// <summary>
/// Policy provider that dynamically creates authorization policies for permission requirements.
/// Intercepts policy names prefixed with "Permission:" and builds policies with PermissionRequirement.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Organization-scoped first: its prefix is distinct, but checking it
        // ahead of the flat one keeps the ordering obvious to anyone adding a
        // third prefix later.
        if (policyName.StartsWith(RequireOrganizationPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var payload = policyName[RequireOrganizationPermissionAttribute.PolicyPrefix.Length..];
            var separator = payload.LastIndexOf(RequireOrganizationPermissionAttribute.PolicySeparator);

            // The separator is appended unconditionally by the attribute, so its
            // absence means a hand-written policy name. Treat the whole payload
            // as the permission and fall back to the conventional route names.
            var permission = separator >= 0 ? payload[..separator] : payload;
            var routeParameterName = separator >= 0 && separator < payload.Length - 1
                ? payload[(separator + 1)..]
                : null;

            var organizationPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new OrganizationPermissionRequirement(permission, routeParameterName))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(organizationPolicy);
        }

        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallbackPolicyProvider.GetFallbackPolicyAsync();
}
