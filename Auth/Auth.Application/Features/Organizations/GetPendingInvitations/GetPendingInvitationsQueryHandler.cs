using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetPendingInvitations;

/// <summary>
/// Handler for getting pending invitations for an organization.
/// </summary>
public class GetPendingInvitationsQueryHandler : IRequestHandler<GetPendingInvitationsQuery, ErrorOr<IReadOnlyList<OrganizationInvitationDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;

    public GetPendingInvitationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationInvitationDto>>> Handle(
        GetPendingInvitationsQuery request,
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

        // Get pending invitations
        var invitations = await _organizationRepository.GetPendingInvitationsAsync(
            request.OrganizationId,
            cancellationToken);

        var invitationDtos = new List<OrganizationInvitationDto>();

        foreach (var invitation in invitations)
        {
            var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
            var inviter = await _userRepository.GetByIdAsync(invitation.InvitedBy, cancellationToken);

            invitationDtos.Add(new OrganizationInvitationDto
            {
                Id = invitation.Id,
                OrganizationId = invitation.OrganizationId,
                OrganizationName = organization.Name,
                OrganizationLogoUrl = organization.LogoUrl,
                Email = invitation.Email,
                RoleId = invitation.RoleId,
                RoleCode = role?.Code ?? string.Empty,
                RoleName = role?.Name ?? string.Empty,
                Status = invitation.Status.ToString(),
                ExpiresAt = invitation.ExpiresAt,
                IsExpired = invitation.IsExpired(),
                InvitedBy = invitation.InvitedBy,
                InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : null,
                InvitedByEmail = inviter?.Email,
                AcceptedAt = invitation.AcceptedAt,
                AcceptedByUserId = invitation.AcceptedByUserId,
                CreatedAt = invitation.CreatedAt
            });
        }

        // Sort in memory: role name and inviter are enrichment values resolved
        // per-row above. Null keeps the repository's CreatedAt DESC order.
        return SortHelper
            .Apply(invitationDtos, request.SortBy, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<OrganizationInvitationDto, object?>> SortSelectors =
        SortHelper.Selectors<OrganizationInvitationDto>(
            (SortFields.OrganizationInvitations.Email, dto => dto.Email),
            (SortFields.OrganizationInvitations.RoleName, dto => dto.RoleName),
            (SortFields.OrganizationInvitations.RoleCode, dto => dto.RoleCode),
            (SortFields.OrganizationInvitations.Status, dto => dto.Status),
            (SortFields.OrganizationInvitations.IsExpired, dto => dto.IsExpired),
            (SortFields.OrganizationInvitations.InvitedByName, dto => dto.InvitedByName),
            (SortFields.OrganizationInvitations.InvitedByEmail, dto => dto.InvitedByEmail),
            (SortFields.OrganizationInvitations.AcceptedAt, dto => dto.AcceptedAt),
            (SortFields.OrganizationInvitations.CreatedAt, dto => dto.CreatedAt),
            (SortFields.OrganizationInvitations.ExpiresAt, dto => dto.ExpiresAt));
}
