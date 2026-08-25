using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.UpdateRole;

/// <summary>
/// Handler for updating an existing role.
/// </summary>
public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, ErrorOr<RoleDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<UpdateRoleCommandHandler> _logger;

    public UpdateRoleCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IPublisher publisher,
        ILogger<UpdateRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<RoleDto>> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.Id);
        }

        // Cannot update system roles
        if (role.IsSystem)
        {
            return Error.Forbidden(
                code: "Role.CannotUpdateSystemRole",
                description: "System roles cannot be modified.");
        }

        // Read before writing. Update mutates the entity in place, so after the
        // call there is nothing left to say what the role used to be called —
        // and "what it was before" is half of what an audit row is for.
        var oldName = role.Name;
        var oldDescription = role.Description;

        // Update role
        role.Update(request.Name, request.Description, request.ModifiedBy);
        await _roleRepository.UpdateAsync(role, cancellationToken);

        // Get permissions
        var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);

        _logger.LogInformation(
            "Role updated: {RoleId} by {ModifiedBy}",
            role.Id, request.ModifiedBy);

        await _publisher.Publish(
            new RoleUpdatedEvent(
                role.Id,
                role.Code,
                oldName,
                role.Name,
                oldDescription,
                role.Description,
                request.ModifiedBy),
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
            Permissions = permissions.Select(p => p.Code.Value).ToList()
        };
    }
}
