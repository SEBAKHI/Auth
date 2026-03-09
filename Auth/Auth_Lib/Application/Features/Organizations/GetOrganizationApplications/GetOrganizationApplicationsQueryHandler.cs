using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.GetOrganizationApplications;

/// <summary>
/// Handler for getting all enabled applications for an organization.
/// </summary>
public class GetOrganizationApplicationsQueryHandler : IRequestHandler<GetOrganizationApplicationsQuery, ErrorOr<IReadOnlyList<OrganizationApplicationDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetOrganizationApplicationsQueryHandler> _logger;

    public GetOrganizationApplicationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        ILogger<GetOrganizationApplicationsQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
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
                    SubscriptionTier = orgApp.SubscriptionTier,
                    EnabledAt = orgApp.EnabledAt,
                    EnabledBy = orgApp.EnabledBy,
                    ExpiresAt = orgApp.ExpiresAt,
                    IsActive = orgApp.IsActive
                });
            }
        }

        _logger.LogDebug("Retrieved {Count} applications for organization {OrganizationId}", dtos.Count, request.OrganizationId);

        return dtos;
    }
}
