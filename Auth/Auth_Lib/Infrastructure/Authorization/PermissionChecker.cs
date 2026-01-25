using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Interfaces.Repositories;

namespace Auth_Lib.Infrastructure.Authorization;

/// <summary>
/// Implementation of permission checking with wildcard support.
/// Supports both direct user permissions (backward compatible) and organization-based permissions.
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IOrganizationRepository _organizationRepository;

    public PermissionChecker(
        IPermissionRepository permissionRepository,
        IOrganizationRepository organizationRepository)
    {
        _permissionRepository = permissionRepository;
        _organizationRepository = organizationRepository;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var permissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return PermissionMatches(permissions, permission);
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyPermissionAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return permissions.Any(p => PermissionMatches(userPermissions, p));
    }

    /// <inheritdoc />
    public async Task<bool> HasAllPermissionsAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return permissions.All(p => PermissionMatches(userPermissions, p));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Get direct user permissions (backward compatible)
        IReadOnlyList<string> directPermissions;
        if (applicationId.HasValue)
        {
            directPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
                userId, applicationId.Value, cancellationToken);
        }
        else
        {
            directPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(userId, cancellationToken);
        }

        foreach (var permission in directPermissions)
        {
            allPermissions.Add(permission);
        }

        // 2. Get organization-based permissions (if applicationId is specified)
        if (applicationId.HasValue)
        {
            var orgPermissions = await GetOrganizationBasedPermissionsAsync(
                userId, applicationId.Value, cancellationToken);

            foreach (var permission in orgPermissions)
            {
                allPermissions.Add(permission);
            }
        }

        return allPermissions.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets permissions for a user through their organization memberships for a specific application.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetOrganizationBasedPermissionsAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get all active organization memberships for the user
        var memberships = await _organizationRepository.GetUserMembershipsAsync(userId, cancellationToken);

        foreach (var membership in memberships)
        {
            // Check if the org has this application enabled
            var isAppEnabled = await _organizationRepository.IsApplicationEnabledAsync(
                membership.OrganizationId,
                applicationId,
                cancellationToken);

            if (!isAppEnabled)
                continue;

            // Get effective permissions for this user in this org for this app
            var orgPermissions = await _organizationRepository.GetEffectivePermissionCodesAsync(
                membership.OrganizationId,
                userId,
                applicationId,
                cancellationToken);

            foreach (var permission in orgPermissions)
            {
                permissions.Add(permission);
            }
        }

        return permissions.ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks if the user's permissions match the required permission using wildcard logic.
    /// </summary>
    private static bool PermissionMatches(IEnumerable<string> userPermissions, string requiredPermission)
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
