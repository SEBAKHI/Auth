using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.RevokeUserPermission;

/// <summary>
/// Handler for revoking a direct permission from a user.
/// </summary>
public class RevokeUserPermissionCommandHandler : IRequestHandler<RevokeUserPermissionCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeUserPermissionCommandHandler> _logger;

    public RevokeUserPermissionCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IPublisher publisher,
        ILogger<RevokeUserPermissionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _publisher = publisher;
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

        // The grant is (user, permission, scope), and the DELETE is the one place that
        // names it — the same defect RemoveUserRoleCommandHandler had: a scope-blind
        // check passed for an application-scoped grant that the platform-scoped
        // DELETE then missed, and the revocation was reported and audited anyway.
        var revoked = await _userRepository.RevokePermissionAsync(
            request.UserId, request.PermissionId, request.ApplicationId, cancellationToken);
        if (!revoked)
        {
            return PermissionErrors.PermissionNotGranted;
        }

        _logger.LogInformation(
            "Permission revoked from user: User {UserId}, Permission {PermissionId} ({PermissionCode}) by {RevokedBy}",
            request.UserId, request.PermissionId, permission.Code, request.RevokedBy);

        await _publisher.Publish(
            new UserPermissionRevokedEvent(
                request.UserId,
                request.PermissionId,
                permission.Code.Value,
                request.ApplicationId,
                request.RevokedBy),
            cancellationToken);

        return true;
    }
}
