using Auth.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace Auth_API.Authorization;

/// <summary>
/// Handler that checks if user has the required permission.
/// Checks JWT claims first for efficiency, avoiding database calls.
/// </summary>
public class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionRequirementHandler> _logger;

    public PermissionRequirementHandler(ILogger<PermissionRequirementHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(JwtClaimNames.Subject)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogDebug("No valid user ID claim found for permission check");
            return Task.CompletedTask; // Not authenticated
        }

        // Get permissions from JWT claims (they're already embedded in the token)
        var userPermissions = context.User.FindAll(JwtClaimNames.Permissions)
            .Select(c => c.Value)
            .ToList();

        var hasPermission = PermissionMatches(userPermissions, requirement.Permission);

        if (hasPermission)
        {
            _logger.LogDebug(
                "User {UserId} has permission {Permission}",
                userId, requirement.Permission);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "User {UserId} denied access - missing permission {Permission}. User permissions: [{UserPermissions}]",
                userId, requirement.Permission, string.Join(", ", userPermissions));
        }

        return Task.CompletedTask;
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
