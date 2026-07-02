using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationMembers;

/// <summary>
/// Handler for getting paginated members of an organization.
/// </summary>
public class GetOrganizationMembersQueryHandler : IRequestHandler<GetOrganizationMembersQuery, ErrorOr<PagedOrganizationMembersDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;

    public GetOrganizationMembersQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
    }

    public async Task<ErrorOr<PagedOrganizationMembersDto>> Handle(
        GetOrganizationMembersQuery request,
        CancellationToken cancellationToken)
    {
        // Check organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Check requester is a member
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId,
            request.RequestedBy,
            cancellationToken);

        if (membership == null)
        {
            return OrganizationErrors.NotMember(request.RequestedBy, request.OrganizationId);
        }

        // Get paginated members
        var (members, totalCount) = await _organizationRepository.GetMembersPagedAsync(
            request.OrganizationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var memberDtos = new List<OrganizationMemberDto>();

        foreach (var member in members)
        {
            var user = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
            var role = await _roleRepository.GetByIdAsync(member.RoleId, cancellationToken);
            var inviter = await _userRepository.GetByIdAsync(member.InvitedBy, cancellationToken);

            memberDtos.Add(new OrganizationMemberDto
            {
                Id = member.Id,
                OrganizationId = member.OrganizationId,
                UserId = member.UserId,
                Email = user?.Email ?? string.Empty,
                FirstName = user?.FirstName,
                LastName = user?.LastName,
                FullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
                RoleId = member.RoleId,
                RoleCode = role?.Code ?? string.Empty,
                RoleName = role?.Name ?? string.Empty,
                IsActive = member.IsActive,
                JoinedAt = member.JoinedAt,
                InvitedBy = member.InvitedBy,
                InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : null,
                ExpiresAt = member.ExpiresAt
            });
        }

        return new PagedOrganizationMembersDto
        {
            Members = memberDtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
