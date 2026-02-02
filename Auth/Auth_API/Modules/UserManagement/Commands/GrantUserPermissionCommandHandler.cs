using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Handler for granting a direct permission to a user.
/// </summary>
public class GrantUserPermissionCommandHandler : IRequestHandler<GrantUserPermissionCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<GrantUserPermissionCommandHandler> _logger;

    public GrantUserPermissionCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        ILogger<GrantUserPermissionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
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
