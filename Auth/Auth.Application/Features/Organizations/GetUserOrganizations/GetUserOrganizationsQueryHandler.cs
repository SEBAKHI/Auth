using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
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

        // Sort in memory: the list is assembled per-membership above (role name
        // and member count are computed). The SQL has no ORDER BY, so default to
        // name for a deterministic order.
        return SortHelper
            .Apply(result, request.SortBy ?? SortFields.UserOrganizations.Name, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<OrganizationSummaryDto, object?>> SortSelectors =
        SortHelper.Selectors<OrganizationSummaryDto>(
            (SortFields.UserOrganizations.Name, dto => dto.Name),
            (SortFields.UserOrganizations.Code, dto => dto.Code),
            (SortFields.UserOrganizations.RoleName, dto => dto.UserRole),
            (SortFields.UserOrganizations.MemberCount, dto => dto.MemberCount));
}
