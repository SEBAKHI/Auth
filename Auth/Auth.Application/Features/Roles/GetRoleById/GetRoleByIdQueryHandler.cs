using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleById;

/// <summary>
/// Handler for getting a role by ID.
/// </summary>
public class GetRoleByIdQueryHandler : IRequestHandler<GetRoleByIdQuery, ErrorOr<RoleDto>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    public GetRoleByIdQueryHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<ErrorOr<RoleDto>> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.Id);
        }

        var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);

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
            Permissions = permissions.Select(p => (string)p.Code).ToList()
        };
    }
}
