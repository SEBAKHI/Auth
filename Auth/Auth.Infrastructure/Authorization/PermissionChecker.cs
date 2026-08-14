using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;

namespace Auth.Infrastructure.Authorization;

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
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        var permissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return PermissionMatches(permissions, permission);
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyPermissionAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return permissions.Any(p => PermissionMatches(userPermissions, p));
    }

    /// <inheritdoc />
    public async Task<bool> HasAllPermissionsAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        var userPermissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        return permissions.All(p => PermissionMatches(userPermissions, p));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid? applicationId,
        CancellationToken cancellationToken)
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

        // 2. Get organization-based permissions (if applicationId is specified).
        // One query across every membership: this runs on the token-mint path,
        // where walking memberships one at a time cost 1 + 2N round trips per
        // sign-in.
        if (applicationId.HasValue)
        {
            var orgPermissions = await _organizationRepository.GetEffectivePermissionCodesForApplicationAsync(
                userId, applicationId.Value, cancellationToken);

            foreach (var permission in orgPermissions)
            {
                allPermissions.Add(permission);
            }
        }

        return allPermissions.ToList().AsReadOnly();
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
