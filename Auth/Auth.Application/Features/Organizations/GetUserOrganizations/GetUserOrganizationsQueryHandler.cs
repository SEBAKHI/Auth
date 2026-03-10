using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetUserOrganizations;

/// <summary>
/// Handler for getting all organizations a user is a member of.
/// </summary>
public class GetUserOrganizationsQueryHandler : IRequestHandler<GetUserOrganizationsQuery, ErrorOr<IReadOnlyList<OrganizationSummaryDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;

    public GetUserOrganizationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationSummaryDto>>> Handle(
        GetUserOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var memberships = await _organizationRepository.GetUserMembershipsAsync(request.UserId, cancellationToken);
        var result = new List<OrganizationSummaryDto>();

        foreach (var membership in memberships)
        {
            var organization = await _organizationRepository.GetByIdAsync(membership.OrganizationId, cancellationToken);
            if (organization == null || !organization.IsActive)
                continue;

            var role = await _roleRepository.GetByIdAsync(membership.RoleId, cancellationToken);
            var members = await _organizationRepository.GetMembersAsync(membership.OrganizationId, cancellationToken);

            result.Add(new OrganizationSummaryDto
            {
                Id = organization.Id,
                Code = organization.Code,
                Name = organization.Name,
                LogoUrl = organization.LogoUrl,
                IsActive = organization.IsActive,
                UserRole = role?.Name,
                MemberCount = members.Count
            });
        }

        return result;
    }
}
