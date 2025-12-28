using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Interfaces.Repositories;

namespace Auth_Lib.Infrastructure.Authorization;

/// <summary>
/// Implementation of permission checking with wildcard support.
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionRepository _permissionRepository;

    public PermissionChecker(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
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
        if (applicationId.HasValue)
        {
            return await _permissionRepository.GetUserEffectivePermissionsAsync(
                userId, applicationId.Value, cancellationToken);
        }

        return await _permissionRepository.GetUserEffectivePermissionsAsync(userId, cancellationToken);
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
