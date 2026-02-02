using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Handler for updating an organization's application subscription settings.
/// </summary>
public class UpdateOrganizationApplicationCommandHandler : IRequestHandler<UpdateOrganizationApplicationCommand, ErrorOr<OrganizationApplicationDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<UpdateOrganizationApplicationCommandHandler> _logger;

    public UpdateOrganizationApplicationCommandHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        ILogger<UpdateOrganizationApplicationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationApplicationDto>> Handle(UpdateOrganizationApplicationCommand request, CancellationToken cancellationToken)
    {
        // Verify organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        // Check if application is enabled for this organization
        var orgApp = await _organizationRepository.GetApplicationSubscriptionAsync(request.OrganizationId, request.ApplicationId, cancellationToken);
        if (orgApp == null)
        {
            return ApplicationErrors.NotEnabledForOrganization;
        }

        // Update the subscription settings via entity methods
        if (request.SubscriptionTier != null && request.SubscriptionTier != orgApp.SubscriptionTier)
        {
            orgApp.UpdateTier(request.SubscriptionTier, request.ModifiedBy);
        }
        if (request.ExpiresAt.HasValue && request.ExpiresAt != orgApp.ExpiresAt)
        {
            orgApp.ExtendExpiration(request.ExpiresAt.Value, request.ModifiedBy);
        }
        if (request.IsActive.HasValue && request.IsActive.Value != orgApp.IsActive)
        {
            if (request.IsActive.Value)
                orgApp.Activate(request.ModifiedBy);
            else
                orgApp.Deactivate(request.ModifiedBy);
        }

        await _organizationRepository.UpdateApplicationSubscriptionAsync(orgApp, cancellationToken);

        // Fetch the updated record
        var updatedOrgApp = await _organizationRepository.GetApplicationSubscriptionAsync(request.OrganizationId, request.ApplicationId, cancellationToken);

        _logger.LogInformation(
            "Organization application updated: Org {OrganizationId}, App {ApplicationId} ({AppCode}) by {ModifiedBy}",
            request.OrganizationId, request.ApplicationId, application.Code, request.ModifiedBy);

        return new OrganizationApplicationDto
        {
            Id = updatedOrgApp!.Id,
            OrganizationId = updatedOrgApp.OrganizationId,
            ApplicationId = updatedOrgApp.ApplicationId,
            ApplicationCode = application.Code,
            ApplicationName = application.Name,
            ApplicationDescription = application.Description,
            SubscriptionTier = updatedOrgApp.SubscriptionTier,
            EnabledAt = updatedOrgApp.EnabledAt,
            EnabledBy = updatedOrgApp.EnabledBy,
            ExpiresAt = updatedOrgApp.ExpiresAt,
            IsActive = updatedOrgApp.IsActive
        };
    }
}
