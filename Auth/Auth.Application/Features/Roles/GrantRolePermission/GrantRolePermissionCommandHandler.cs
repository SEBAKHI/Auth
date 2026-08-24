using Auth.Domain.Events;
using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GrantRolePermission;

/// <summary>
/// Handler for adding a permission to a role.
/// </summary>
public class GrantRolePermissionCommandHandler
    : IRequestHandler<GrantRolePermissionCommand, ErrorOr<Success>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly IPublisher _publisher;
    private readonly ILogger<GrantRolePermissionCommandHandler> _logger;

    public GrantRolePermissionCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        IPublisher publisher,
        ILogger<GrantRolePermissionCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        GrantRolePermissionCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        var permission = await _permissionRepository.GetByIdAsync(
            request.PermissionId, cancellationToken);
        if (permission is null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        if (!permission.IsActive)
        {
            // Retired codes stay in the table so their foreign keys hold, but
            // they open nothing. Granting one would look like an act and be
            // none.
            return PermissionErrors.PermissionInactive;
        }

        // The same rule the user-facing grant obeys, for the same reason: a role
        // is a bundle of permissions and assigning it hands them all over, so
        // stocking a role with what you do not hold is granting it by proxy.
        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.GrantedBy, [permission.Code.Value], cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked adding {PermissionCode} to role {RoleId}: actor {GrantedBy} does not hold it",
                permission.Code.Value, request.RoleId, request.GrantedBy);
            return canGrant.Errors;
        }

        var existing = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);
        if (existing.Any(p => p.Id == request.PermissionId))
        {
            return PermissionErrors.PermissionAlreadyGranted;
        }

        await _permissionRepository.GrantToRoleAsync(
            RolePermission.Create(request.RoleId, request.PermissionId, request.GrantedBy),
            cancellationToken);

        _logger.LogInformation(
            "Permission {PermissionCode} added to role {RoleId} ({RoleName}) by {GrantedBy}",
            permission.Code.Value, role.Id, role.Name, request.GrantedBy);

        // Wider than a direct grant: it reaches everyone holding the role, now
        // and in future, without another action being taken.
        await _publisher.Publish(
            new RolePermissionGrantedEvent(
                request.RoleId,
                role.Name,
                request.PermissionId,
                permission.Code.Value,
                request.GrantedBy),
            cancellationToken);

        return Result.Success;
    }
}
