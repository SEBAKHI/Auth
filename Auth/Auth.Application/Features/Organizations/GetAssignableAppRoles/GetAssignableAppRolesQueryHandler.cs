using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAssignableAppRoles;

/// <summary>
/// Handler for the organization's application-role picker.
/// </summary>
public class GetAssignableAppRolesQueryHandler
    : IRequestHandler<GetAssignableAppRolesQuery, ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly OrganizationGrantGuard _grantGuard;

    public GetAssignableAppRolesQueryHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        OrganizationGrantGuard grantGuard)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>> Handle(
        GetAssignableAppRolesQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Limited to the organization's own applications: the same refusal the
        // assignment returns for an application the organization has not enabled.
        if (!await _organizationRepository.IsApplicationEnabledAsync(
                request.OrganizationId, request.ApplicationId, cancellationToken))
        {
            return OrganizationErrors.ApplicationNotEnabled(request.ApplicationId);
        }

        var roles = await _roleRepository.GetByApplicationAsync(
            request.ApplicationId, sortBy: null, SortDirection.Asc, cancellationToken);

        // One read of every role's permissions and one read of the caller's authority,
        // then the assignment's own coverage test per role.
        var permissionCodes = (await _permissionRepository.GetApplicationRolePermissionCodesAsync(
                request.ApplicationId, cancellationToken))
            .ToLookup(row => row.RoleId, row => row.Code);

        var heldCodes = await _grantGuard.GetHeldCodesAsync(
            request.OrganizationId,
            request.RequestedBy,
            request.ApplicationId,
            request.PlatformAuthorityInToken,
            cancellationToken);

        return roles
            .Where(role => role.IsActive)
            .Where(role => OrganizationGrantGuard.Covers(heldCodes, permissionCodes[role.Id]))
            .Select(role => new OrganizationRoleOptionDto
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name,
                Description = role.Description
            })
            .ToList();
    }
}
