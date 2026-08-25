using Auth.Domain.Events;
using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.CreateRole;

/// <summary>
/// Handler for creating a new role.
/// </summary>
public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, ErrorOr<RoleDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateRoleCommandHandler> _logger;

    public CreateRoleCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        PermissionGrantGuard grantGuard,
        IPublisher publisher,
        ILogger<CreateRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<RoleDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate code
        if (await _roleRepository.ExistsByCodeAsync(request.ApplicationId, request.Code, cancellationToken))
        {
            return RoleErrors.DuplicateCode(request.Code, request.ApplicationId ?? Guid.Empty);
        }

        // No amplification. Creating a role stocked with permissions the creator
        // does not hold would launder them: the role is assignable afterwards,
        // so the restriction on granting would be one indirection away from
        // meaningless. Checked BEFORE the role is written, so a refusal leaves
        // nothing behind — the permission loop below is not transactional.
        var requestedCodes = new List<string>();
        if (request.PermissionIds is { Count: > 0 })
        {
            foreach (var permissionId in request.PermissionIds)
            {
                var requested = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
                if (requested is not null)
                {
                    requestedCodes.Add(requested.Code);
                }
            }

            var canGrant = await _grantGuard.EnsureCanGrantAsync(
                request.CreatedBy, requestedCodes, cancellationToken);
            if (canGrant.IsError)
            {
                _logger.LogWarning(
                    "Blocked creation of role {RoleCode}: actor {CreatedBy} does not hold every permission requested for it",
                    request.Code, request.CreatedBy);
                return canGrant.Errors;
            }
        }

        // Create role
        var role = Role.Create(
            request.ApplicationId,
            request.Code,
            request.Name,
            request.Description,
            request.CreatedBy);

        await _roleRepository.CreateAsync(role, cancellationToken);

        // Assign permissions if provided
        var permissionCodes = new List<string>();
        if (request.PermissionIds != null && request.PermissionIds.Count > 0)
        {
            foreach (var permissionId in request.PermissionIds)
            {
                var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
                if (permission != null)
                {
                    var rolePermission = RolePermission.Create(role.Id, permissionId, request.CreatedBy);
                    await _permissionRepository.GrantToRoleAsync(rolePermission, cancellationToken);
                    permissionCodes.Add(permission.Code);
                }
            }
        }

        _logger.LogInformation(
            "Role created: {RoleId} ({RoleCode}) for application {ApplicationId} by {CreatedBy}",
            role.Id, role.Code, request.ApplicationId, request.CreatedBy);

        await _publisher.Publish(
            new RoleCreatedEvent(role.Id, role.Code, role.Name, request.ApplicationId, request.CreatedBy),
            cancellationToken);

        return new RoleDto
        {
            Id = role.Id,
            ApplicationId = role.ApplicationId,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            IsActive = role.IsActive,
            CreatedAt = role.CreatedAt,
            ModifiedAt = role.ModifiedAt,
            Permissions = permissionCodes
        };
    }
}
