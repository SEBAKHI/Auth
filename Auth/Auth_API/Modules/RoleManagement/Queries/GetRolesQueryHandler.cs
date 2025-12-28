using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.RoleManagement.Queries;

/// <summary>
/// Handler for getting roles.
/// </summary>
public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, ErrorOr<IReadOnlyList<RoleDto>>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    public GetRolesQueryHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Role> roles;

        if (request.ApplicationId.HasValue)
        {
            roles = await _roleRepository.GetByApplicationAsync(request.ApplicationId.Value, cancellationToken);
        }
        else
        {
            // For now, return empty list if no application is specified
            // In a real implementation, you might want to get all roles across applications
            roles = [];
        }

        var roleDtos = new List<RoleDto>();
        foreach (var role in roles)
        {
            var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);
            roleDtos.Add(new RoleDto
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
            });
        }

        return roleDtos;
    }
}
