using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoles;

/// <summary>
/// Handler for getting roles.
/// </summary>
public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, ErrorOr<IReadOnlyList<RoleDto>>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;

    public GetRolesQueryHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Role> roles;

        if (request.ApplicationId.HasValue)
        {
            roles = await _roleRepository.GetByApplicationAsync(
                request.ApplicationId.Value, request.SortBy, request.SortDirection, cancellationToken);
        }
        else
        {
            roles = await _roleRepository.GetAllAsync(
                request.SortBy, request.SortDirection, cancellationToken);
        }

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository,
            roles.Select(role => role.ApplicationId),
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            roles.SelectMany(role => new Guid?[] { role.CreatedBy, role.ModifiedBy }),
            cancellationToken);

        var roleDtos = new List<RoleDto>();
        foreach (var role in roles)
        {
            var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);
            roleDtos.Add(new RoleDto
            {
                Id = role.Id,
                ApplicationId = role.ApplicationId,
                ApplicationName = role.ApplicationId.HasValue
                    ? applicationNames.GetValueOrDefault(role.ApplicationId.Value)
                    : null,
                Code = role.Code,
                Name = role.Name,
                Description = role.Description,
                IsSystem = role.IsSystem,
                IsActive = role.IsActive,
                CreatedAt = role.CreatedAt,
                CreatedBy = role.CreatedBy,
                CreatedByName = userNames.GetValueOrDefault(role.CreatedBy),
                ModifiedAt = role.ModifiedAt,
                ModifiedBy = role.ModifiedBy,
                ModifiedByName = role.ModifiedBy.HasValue
                    ? userNames.GetValueOrDefault(role.ModifiedBy.Value)
                    : null,
                Permissions = permissions.Select(p => (string)p.Code).ToList()
            });
        }

        return roleDtos;
    }
}
