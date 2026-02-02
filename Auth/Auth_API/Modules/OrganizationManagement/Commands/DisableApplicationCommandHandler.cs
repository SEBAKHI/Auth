using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Handler for disabling an application for an organization.
/// </summary>
public class DisableApplicationCommandHandler : IRequestHandler<DisableApplicationCommand, ErrorOr<bool>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<DisableApplicationCommandHandler> _logger;

    public DisableApplicationCommandHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        ILogger<DisableApplicationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(DisableApplicationCommand request, CancellationToken cancellationToken)
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
        var isEnabled = await _organizationRepository.IsApplicationEnabledAsync(request.OrganizationId, request.ApplicationId, cancellationToken);
        if (!isEnabled)
        {
            return ApplicationErrors.NotEnabledForOrganization;
        }

        await _organizationRepository.DisableApplicationAsync(request.OrganizationId, request.ApplicationId, cancellationToken);

        _logger.LogInformation(
            "Application disabled for organization: Org {OrganizationId}, App {ApplicationId} ({AppCode}) by {DisabledBy}",
            request.OrganizationId, request.ApplicationId, application.Code, request.DisabledBy);

        return true;
    }
}
