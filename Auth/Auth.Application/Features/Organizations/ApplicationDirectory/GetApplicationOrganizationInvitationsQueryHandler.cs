using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>Handles <see cref="GetApplicationOrganizationInvitationsQuery"/>.</summary>
/// <remarks>
/// The organization's own invitation query (OI-193), narrowed to the calling application: a
/// membership-only invitation or another application's never reaches it. Effective status by
/// the same rule and the same clock reading as that list. Role codes are read once per distinct
/// role on the page, not once per row.
/// </remarks>
public sealed class GetApplicationOrganizationInvitationsQueryHandler
    : IRequestHandler<GetApplicationOrganizationInvitationsQuery, ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>>>
{
    private readonly ApplicationDirectoryAccess _access;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetApplicationOrganizationInvitationsQueryHandler> _logger;

    public GetApplicationOrganizationInvitationsQueryHandler(
        ApplicationDirectoryAccess access,
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        TimeProvider timeProvider,
        ILogger<GetApplicationOrganizationInvitationsQueryHandler> logger)
    {
        _access = access;
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>>> Handle(
        GetApplicationOrganizationInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var grant = await _access.ResolveAsync(
            request.Caller, request.OrganizationId, PermissionCodes.Org.MembersInvite, cancellationToken);
        if (grant.IsError)
        {
            return grant.Errors;
        }

        var statuses = request.Statuses is { Count: > 0 }
            ? request.Statuses
            : GetApplicationOrganizationInvitationsQuery.DefaultStatuses;

        // One clock reading: the SQL that filters by effective status and the rows built below
        // decide every invitation against the same instant.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (invitations, totalCount) = await _organizationRepository.GetInvitationsPagedAsync(
            request.OrganizationId,
            new InvitationListFilter(statuses, grant.Value.Application.Id),
            now,
            request.PageNumber,
            request.PageSize,
            sortBy: null,
            Domain.Enums.SortDirection.Asc,
            cancellationToken);

        var roleCodes = await RoleCodesAsync(
            invitations.SelectMany(invitation => new[] { (Guid?)invitation.RoleId, invitation.ApplicationRole?.RoleId }),
            cancellationToken);

        var items = invitations
            // The filter already guarantees it; kept so that no change to the query can hand an
            // application an invitation that is not its own.
            .Where(invitation => invitation.ApplicationRole?.ApplicationId == grant.Value.Application.Id)
            .Select(invitation => new ApplicationOrganizationInvitationResponse
            {
                Id = invitation.Id,
                Email = invitation.Email,
                MembershipRole = roleCodes.GetValueOrDefault(invitation.RoleId) ?? string.Empty,
                ApplicationRole = roleCodes.GetValueOrDefault(invitation.ApplicationRole!.RoleId) ?? string.Empty,
                Status = invitation.EffectiveStatus(now),
                CreatedAt = invitation.CreatedAt,
                ExpiresAt = invitation.ExpiresAt,
                AcceptedAt = invitation.AcceptedAt,
                AcceptedUserId = invitation.AcceptedByUserId,
                ApplicationRoleOutcome = invitation.ApplicationRoleOutcome
            })
            .ToList();

        ApplicationDirectoryLog.Read(_logger, grant.Value, "invitations", items.Count);

        return new ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private async Task<IReadOnlyDictionary<Guid, string>> RoleCodesAsync(
        IEnumerable<Guid?> roleIds,
        CancellationToken cancellationToken)
    {
        var codes = new Dictionary<Guid, string>();
        foreach (var roleId in roleIds.OfType<Guid>().Distinct())
        {
            var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
            if (role is not null)
            {
                codes[roleId] = role.Code;
            }
        }

        return codes;
    }
}
