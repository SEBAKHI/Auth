using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Commands;

/// <summary>
/// Handler for creating a new role.
/// </summary>
public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, ErrorOr<RoleDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<CreateRoleCommandHandler> _logger;

    public CreateRoleCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        ILogger<CreateRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<RoleDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate code
        if (await _roleRepository.ExistsByCodeAsync(request.ApplicationId, request.Code, cancellationToken))
        {
            return RoleErrors.DuplicateCode(request.Code, request.ApplicationId);
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
