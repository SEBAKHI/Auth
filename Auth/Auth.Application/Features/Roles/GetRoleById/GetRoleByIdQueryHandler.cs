using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
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
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;

    public GetRoleByIdQueryHandler(
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

    public async Task<ErrorOr<RoleDto>> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.Id);
        }

        var permissions = await _permissionRepository.GetRolePermissionsAsync(role.Id, cancellationToken);
        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository, [role.ApplicationId], cancellationToken);
        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [role.CreatedBy, role.ModifiedBy], cancellationToken);

        return new RoleDto
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
        };
    }
}
