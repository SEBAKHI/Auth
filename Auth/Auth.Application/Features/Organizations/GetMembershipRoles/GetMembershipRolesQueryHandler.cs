using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetMembershipRoles;

/// <summary>
/// Handler for the organization's membership-role picker.
/// </summary>
public class GetMembershipRolesQueryHandler
    : IRequestHandler<GetMembershipRolesQuery, ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;

    public GetMembershipRolesQueryHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationRoleOptionDto>>> Handle(
        GetMembershipRolesQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        var roles = await _roleRepository.GetGlobalByCodesAsync(
            OrganizationRoleCodes.AssignableMembershipRoles, cancellationToken);

        // The same predicate the invitation and the role change check on save.
        return roles
            .Where(role => role.IsAssignableMembershipRole())
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
