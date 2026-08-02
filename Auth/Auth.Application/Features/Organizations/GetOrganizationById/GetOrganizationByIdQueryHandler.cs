using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationById;

/// <summary>
/// Handler for getting organization details by ID.
/// </summary>
public class GetOrganizationByIdQueryHandler : IRequestHandler<GetOrganizationByIdQuery, ErrorOr<OrganizationDetailDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetOrganizationByIdQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<OrganizationDetailDto>> Handle(
        GetOrganizationByIdQuery request,
        CancellationToken cancellationToken)
    {
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

        // Get owner info
        var owner = await _userRepository.GetByIdAsync(organization.OwnerId, cancellationToken);

        // Get all members with their info
        var members = await _organizationRepository.GetMembersAsync(request.OrganizationId, cancellationToken);
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
                Email = user?.Email?.Value ?? string.Empty,
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

        // Get enabled applications
        var apps = await _organizationRepository.GetEnabledApplicationsAsync(request.OrganizationId, cancellationToken);
        var assignedUserCounts = await _organizationRepository.GetAssignedUserCountsAsync(request.OrganizationId, cancellationToken);
        var appDtos = new List<OrganizationApplicationDto>();

        foreach (var app in apps)
        {
            var application = await _applicationRepository.GetByIdAsync(app.ApplicationId, cancellationToken);
            var enabledByUser = await _userRepository.GetByIdAsync(app.EnabledBy, cancellationToken);

            appDtos.Add(new OrganizationApplicationDto
            {
                Id = app.Id,
                OrganizationId = app.OrganizationId,
                ApplicationId = app.ApplicationId,
                ApplicationCode = application?.Code ?? string.Empty,
                ApplicationName = application?.Name ?? string.Empty,
                ApplicationDescription = application?.Description,
                ApplicationLogoUrl = _imageUrlComposer.Compose(application?.LogoUrl),
                IsActive = app.IsActive,
                EnabledAt = app.EnabledAt,
                EnabledBy = app.EnabledBy,
                EnabledByName = enabledByUser != null ? $"{enabledByUser.FirstName} {enabledByUser.LastName}".Trim() : null,
                ExpiresAt = app.ExpiresAt,
                SubscriptionTier = app.SubscriptionTier,
                AssignedUserCount = assignedUserCounts.GetValueOrDefault(app.ApplicationId)
            });
        }

        var auditNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            [organization.CreatedBy, organization.ModifiedBy],
            cancellationToken);

        return new OrganizationDetailDto
        {
            Id = organization.Id,
            Code = organization.Code,
            Name = organization.Name,
            Description = organization.Description,
            LogoUrl = _imageUrlComposer.Compose(organization.LogoUrl),
            Website = organization.Website,
            ContactEmail = organization.ContactEmail,
            OwnerId = organization.OwnerId,
            OwnerName = owner != null ? $"{owner.FirstName} {owner.LastName}".Trim() : null,
            OwnerEmail = owner?.Email?.Value,
            IsActive = organization.IsActive,
            MemberCount = members.Count,
            EnabledAppCount = apps.Count,
            CreatedAt = organization.CreatedAt,
            CreatedBy = organization.CreatedBy,
            CreatedByName = auditNames.GetValueOrDefault(organization.CreatedBy),
            ModifiedAt = organization.ModifiedAt,
            ModifiedBy = organization.ModifiedBy,
            ModifiedByName = organization.ModifiedBy.HasValue
                ? auditNames.GetValueOrDefault(organization.ModifiedBy.Value)
                : null,
            Members = memberDtos,
            EnabledApplications = appDtos
        };
    }
}
