using Auth.Domain.Events;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.RevokeRolePermission;

/// <summary>
/// Handler for removing a permission from a role.
/// </summary>
/// <remarks>
/// Deliberately NOT subject to the no-amplification rule. That rule exists to
/// stop authority spreading beyond its holder; taking authority away spreads
/// nothing, and requiring the remover to hold what they are removing would
/// leave a mis-granted permission un-removable by everyone except a holder of
/// it. Removal is still gated on roles:update, which is the ordinary "may you
/// change this role" question.
/// </remarks>
public class RevokeRolePermissionCommandHandler
    : IRequestHandler<RevokeRolePermissionCommand, ErrorOr<Success>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeRolePermissionCommandHandler> _logger;

    public RevokeRolePermissionCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IPublisher publisher,
        ILogger<RevokeRolePermissionCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeRolePermissionCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        var granted = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);
        var permission = granted.FirstOrDefault(p => p.Id == request.PermissionId);
        if (permission is null)
        {
            return PermissionErrors.PermissionNotGranted;
        }

        await _permissionRepository.RevokeFromRoleAsync(
            request.RoleId, request.PermissionId, cancellationToken);

        _logger.LogInformation(
            "Permission {PermissionCode} removed from role {RoleId} ({RoleName}) by {RevokedBy}",
            permission.Code.Value, role.Id, role.Name, request.RevokedBy);

        await _publisher.Publish(
            new RolePermissionRevokedEvent(
                request.RoleId,
                role.Name,
                request.PermissionId,
                permission.Code.Value,
                request.RevokedBy),
            cancellationToken);

        return Result.Success;
    }
}
