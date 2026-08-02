using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetOrganizationMembersQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _imageUrlComposer = imageUrlComposer;
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

        // Members only — unless the caller administers all organizations.
        if (!request.PlatformScope)
        {
            var membership = await _organizationRepository.GetMembershipAsync(
                request.OrganizationId,
                request.RequestedBy,
                cancellationToken);

            if (membership == null)
            {
                return OrganizationErrors.NotMember(request.RequestedBy, request.OrganizationId);
            }
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

        // Batch-resolve member/inviter users once instead of per-row lookups.
        var userIds = members
            .SelectMany(member => new[] { member.UserId, member.InvitedBy })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var users = userIds.Count == 0
            ? []
            : await _userRepository.GetByIdsAsync(userIds, cancellationToken) ?? [];
        var usersById = users.ToDictionary(user => user.Id);

        var memberDtos = new List<OrganizationMemberDto>();

        foreach (var member in members)
        {
            var user = usersById.GetValueOrDefault(member.UserId);
            var role = await _roleRepository.GetByIdAsync(member.RoleId, cancellationToken);
            var inviter = usersById.GetValueOrDefault(member.InvitedBy);

            memberDtos.Add(new OrganizationMemberDto
            {
                Id = member.Id,
                OrganizationId = member.OrganizationId,
                UserId = member.UserId,
                Email = user?.Email?.Value ?? string.Empty,
                FirstName = user?.FirstName,
                LastName = user?.LastName,
                FullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
                ProfileImageUrl = _imageUrlComposer.Compose(user?.ProfileImageUrl),
                RoleId = member.RoleId,
                RoleCode = role?.Code ?? string.Empty,
                RoleName = role?.Name ?? string.Empty,
                IsActive = member.IsActive,
                JoinedAt = member.JoinedAt,
                InvitedBy = member.InvitedBy,
                InvitedByName = inviter != null ? NameLookupHelper.DisplayName(inviter) : null,
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
