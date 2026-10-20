using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetPendingInvitations;

/// <summary>
/// Handler for getting one page of an organization's invitations.
/// </summary>
public class GetPendingInvitationsQueryHandler : IRequestHandler<GetPendingInvitationsQuery, ErrorOr<PagedOrganizationInvitationsDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly TimeProvider _timeProvider;

    public GetPendingInvitationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer,
        TimeProvider timeProvider)
    {
        _applicationRepository = applicationRepository;
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _imageUrlComposer = imageUrlComposer;
        _timeProvider = timeProvider;
    }

    public async Task<ErrorOr<PagedOrganizationInvitationsDto>> Handle(
        GetPendingInvitationsQuery request,
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

        // One clock reading for the page: the SQL that filters and sorts by effective status
        // and the DTOs built below decide every row against the same instant.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (invitations, totalCount) = await _organizationRepository.GetInvitationsPagedAsync(
            request.OrganizationId,
            new InvitationListFilter(request.Statuses ?? []),
            now,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        // Batch-resolve inviter/accepter users once instead of per-row lookups.
        var userIds = invitations
            .SelectMany(invitation => new[] { (Guid?)invitation.InvitedBy, invitation.AcceptedByUserId })
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var users = userIds.Count == 0
            ? []
            : await _userRepository.GetByIdsAsync(userIds, cancellationToken) ?? [];
        var usersById = users.ToDictionary(user => user.Id);

        var invitationDtos = new List<OrganizationInvitationDto>();

        foreach (var invitation in invitations)
        {
            var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
            var applicationRole = invitation.ApplicationRole is { } named
                ? await _roleRepository.GetByIdAsync(named.RoleId, cancellationToken)
                : null;
            var application = invitation.ApplicationRole is { } namedApplication
                ? await _applicationRepository.GetByIdAsync(namedApplication.ApplicationId, cancellationToken)
                : null;
            var inviter = usersById.GetValueOrDefault(invitation.InvitedBy);
            var accepter = invitation.AcceptedByUserId.HasValue
                ? usersById.GetValueOrDefault(invitation.AcceptedByUserId.Value)
                : null;
            var status = invitation.EffectiveStatus(now);

            invitationDtos.Add(new OrganizationInvitationDto
            {
                Id = invitation.Id,
                OrganizationId = invitation.OrganizationId,
                OrganizationName = organization.Name,
                OrganizationLogoUrl = _imageUrlComposer.Compose(organization.LogoUrl),
                Email = invitation.Email,
                RoleId = invitation.RoleId,
                RoleCode = role?.Code ?? string.Empty,
                RoleName = role?.Name ?? string.Empty,
                Status = status.ToString(),
                ExpiresAt = invitation.ExpiresAt,
                IsExpired = status == InvitationStatus.Expired,
                CanResend = invitation.CanBeResent(),
                InvitedBy = invitation.InvitedBy,
                InvitedByName = inviter != null ? NameLookupHelper.DisplayName(inviter) : null,
                InvitedByEmail = inviter?.Email?.Value,
                AcceptedAt = invitation.AcceptedAt,
                AcceptedByUserId = invitation.AcceptedByUserId,
                AcceptedByUserName = accepter != null ? NameLookupHelper.DisplayName(accepter) : null,
                CreatedAt = invitation.CreatedAt,
                ApplicationId = invitation.ApplicationRole?.ApplicationId,
                ApplicationName = application?.Name,
                ApplicationRoleId = invitation.ApplicationRole?.RoleId,
                ApplicationRoleName = applicationRole?.Name,
                ApplicationRoleOutcome = invitation.ApplicationRoleOutcome
            });
        }

        // Already in the requested order: the repository sorts and pages in SQL.
        return new PagedOrganizationInvitationsDto
        {
            Invitations = invitationDtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
