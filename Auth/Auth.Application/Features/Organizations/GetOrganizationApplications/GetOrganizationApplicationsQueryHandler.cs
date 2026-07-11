using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetOrganizationApplications;

/// <summary>
/// Handler for getting all enabled applications for an organization.
/// </summary>
public class GetOrganizationApplicationsQueryHandler : IRequestHandler<GetOrganizationApplicationsQuery, ErrorOr<IReadOnlyList<OrganizationApplicationDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetOrganizationApplicationsQueryHandler> _logger;

    public GetOrganizationApplicationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetOrganizationApplicationsQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationApplicationDto>>> Handle(GetOrganizationApplicationsQuery request, CancellationToken cancellationToken)
    {
        // Verify organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Check if user is a member of the organization
        var isMember = await _organizationRepository.IsMemberAsync(request.OrganizationId, request.RequestedBy, cancellationToken);
        if (!isMember)
        {
            return OrganizationErrors.NotAMember;
        }

        var orgApps = await _organizationRepository.GetEnabledApplicationsAsync(request.OrganizationId, cancellationToken);
        var assignedUserCounts = await _organizationRepository.GetAssignedUserCountsAsync(request.OrganizationId, cancellationToken);
        var enabledByNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, orgApps.Select(orgApp => (Guid?)orgApp.EnabledBy), cancellationToken);

        // Enrich with application details
        var dtos = new List<OrganizationApplicationDto>();
        foreach (var orgApp in orgApps)
        {
            var app = await _applicationRepository.GetByIdAsync(orgApp.ApplicationId, cancellationToken);
            if (app != null)
            {
                dtos.Add(new OrganizationApplicationDto
                {
                    Id = orgApp.Id,
                    OrganizationId = orgApp.OrganizationId,
                    ApplicationId = orgApp.ApplicationId,
                    ApplicationCode = app.Code,
                    ApplicationName = app.Name,
                    ApplicationDescription = app.Description,
                    ApplicationLogoUrl = _imageUrlComposer.Compose(app.LogoUrl),
                    SubscriptionTier = orgApp.SubscriptionTier,
                    EnabledAt = orgApp.EnabledAt,
                    EnabledBy = orgApp.EnabledBy,
                    EnabledByName = enabledByNames.GetValueOrDefault(orgApp.EnabledBy),
                    ExpiresAt = orgApp.ExpiresAt,
                    IsActive = orgApp.IsActive,
                    AssignedUserCount = assignedUserCounts.GetValueOrDefault(orgApp.ApplicationId)
                });
            }
        }

        _logger.LogDebug("Retrieved {Count} applications for organization {OrganizationId}", dtos.Count, request.OrganizationId);

        // Sort in memory: application code/name are enrichment values resolved
        // per-row above. The SQL has no ORDER BY, so default to application name
        // for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.OrganizationApplications.ApplicationName, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<OrganizationApplicationDto, object?>> SortSelectors =
        SortHelper.Selectors<OrganizationApplicationDto>(
            (SortFields.OrganizationApplications.ApplicationName, dto => dto.ApplicationName),
            (SortFields.OrganizationApplications.ApplicationCode, dto => dto.ApplicationCode),
            (SortFields.OrganizationApplications.ApplicationDescription, dto => dto.ApplicationDescription),
            (SortFields.OrganizationApplications.SubscriptionTier, dto => dto.SubscriptionTier),
            (SortFields.OrganizationApplications.EnabledAt, dto => dto.EnabledAt),
            (SortFields.OrganizationApplications.ExpiresAt, dto => dto.ExpiresAt),
            (SortFields.OrganizationApplications.IsActive, dto => dto.IsActive),
            (SortFields.OrganizationApplications.AssignedUserCount, dto => dto.AssignedUserCount));
}
