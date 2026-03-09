using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.RevokeUserPermission;

/// <summary>
/// Handler for revoking a direct permission from a user.
/// </summary>
public class RevokeUserPermissionCommandHandler : IRequestHandler<RevokeUserPermissionCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<RevokeUserPermissionCommandHandler> _logger;

    public RevokeUserPermissionCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        ILogger<RevokeUserPermissionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(RevokeUserPermissionCommand request, CancellationToken cancellationToken)
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

        // Check if user has this direct permission
        if (!await _userRepository.HasDirectPermissionAsync(request.UserId, request.PermissionId, cancellationToken))
        {
            return PermissionErrors.PermissionNotGranted;
        }

        await _userRepository.RevokePermissionAsync(request.UserId, request.PermissionId, request.ApplicationId, cancellationToken);

        _logger.LogInformation(
            "Permission revoked from user: User {UserId}, Permission {PermissionId} ({PermissionCode}) by {RevokedBy}",
            request.UserId, request.PermissionId, permission.Code, request.RevokedBy);

        return true;
    }
}
