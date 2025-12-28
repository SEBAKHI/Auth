using Auth_Lib.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Auth_API.Authorization;

/// <summary>
/// Handler that checks if user has the required permission.
/// </summary>
public class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly ILogger<PermissionRequirementHandler> _logger;

    public PermissionRequirementHandler(
        IPermissionChecker permissionChecker,
        ILogger<PermissionRequirementHandler> logger)
    {
        _permissionChecker = permissionChecker;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogDebug("No valid user ID claim found for permission check");
            return; // Not authenticated
        }

        // Get application ID from the JWT 'aud' claim or route
        Guid? applicationId = null;
        var audienceClaim = context.User.FindFirst("aud")?.Value;
        if (!string.IsNullOrEmpty(audienceClaim) && Guid.TryParse(audienceClaim, out var appId))
        {
            applicationId = appId;
        }

        var hasPermission = await _permissionChecker.HasPermissionAsync(
            userId,
            requirement.Permission,
            applicationId);

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
                "User {UserId} denied access - missing permission {Permission}",
                userId, requirement.Permission);
        }
    }
}
