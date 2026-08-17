using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GrantUserPermission;

/// <summary>
/// Handler for granting a direct permission to a user.
/// </summary>
public class GrantUserPermissionCommandHandler : IRequestHandler<GrantUserPermissionCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly ILogger<GrantUserPermissionCommandHandler> _logger;

    public GrantUserPermissionCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        ILogger<GrantUserPermissionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(GrantUserPermissionCommand request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Verify permission exists
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        // No amplification: the endpoint gate asks whether this actor may grant
        // at all, which on its own would let a users:manage-permissions holder
        // hand itself the global "*" row and become super-administrator in one
        // call. This asks the second question — whether it holds what it is
        // handing over.
        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.GrantedBy, [permission.Code.Value], cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked grant of {PermissionCode} to user {UserId}: actor {GrantedBy} does not hold it",
                permission.Code.Value, request.UserId, request.GrantedBy);
            return canGrant.Errors;
        }

        // Check if user already has this direct permission
        if (await _userRepository.HasDirectPermissionAsync(request.UserId, request.PermissionId, cancellationToken))
        {
            return PermissionErrors.PermissionAlreadyGranted;
        }

        // Create and grant the permission
        var userPermission = UserPermission.Create(
            request.UserId,
            request.PermissionId,
            request.GrantedBy,
            request.ApplicationId,
            request.ExpiresAt);

        await _userRepository.GrantPermissionAsync(userPermission, cancellationToken);

        _logger.LogInformation(
            "Permission granted to user: User {UserId}, Permission {PermissionId} ({PermissionCode}) by {GrantedBy}",
            request.UserId, request.PermissionId, permission.Code, request.GrantedBy);

        return true;
    }
}
