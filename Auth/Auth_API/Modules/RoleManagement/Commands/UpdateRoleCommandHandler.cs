using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Commands;

/// <summary>
/// Handler for updating an existing role.
/// </summary>
public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, ErrorOr<RoleDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<UpdateRoleCommandHandler> _logger;

    public UpdateRoleCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        ILogger<UpdateRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
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

        // Update role
        role.Update(request.Name, request.Description, request.ModifiedBy);
        await _roleRepository.UpdateAsync(role, cancellationToken);

        // Get permissions
        var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);

        _logger.LogInformation(
            "Role updated: {RoleId} by {ModifiedBy}",
            role.Id, request.ModifiedBy);

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
            Permissions = permissions.Select(p => p.Code).ToList()
        };
    }
}
