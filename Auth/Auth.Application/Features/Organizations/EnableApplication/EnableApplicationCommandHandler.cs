using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ApplicationEntity = Auth.Domain.Entities.Application;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.EnableApplication;

/// <summary>
/// Handler for enabling an application for an organization.
/// </summary>
public class EnableApplicationCommandHandler : IRequestHandler<EnableApplicationCommand, ErrorOr<OrganizationApplicationDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<EnableApplicationCommandHandler> _logger;

    public EnableApplicationCommandHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository,
        ILogger<EnableApplicationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationApplicationDto>> Handle(
        EnableApplicationCommand request,
        CancellationToken cancellationToken)
    {
        // Check organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        if (!organization.IsActive)
        {
            return OrganizationErrors.Inactive(request.OrganizationId);
        }

        // Check application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return OrganizationErrors.ApplicationNotFound(request.ApplicationId);
        }

        // A restricted application admits only the users on its own access list,
        // so an organization can never enable one. The console never offers a
        // restricted application in its picker; this is the guard that actually
        // enforces it, for anyone calling the API directly.
        if (application.AccessMode == ApplicationAccessMode.Restricted)
        {
            _logger.LogWarning(
                "Refused to enable restricted application {ApplicationId} ({ApplicationCode}) for organization {OrganizationId}",
                application.Id, application.Code, request.OrganizationId);

            return ApplicationErrors.RestrictedCannotBeEnabledForOrganization;
        }

        // Check if already enabled
        var existingSubscription = await _organizationRepository.GetApplicationSubscriptionAsync(
            request.OrganizationId,
            request.ApplicationId,
            cancellationToken);

        if (existingSubscription != null && existingSubscription.IsActive)
        {
            return OrganizationErrors.ApplicationAlreadyEnabled(request.ApplicationId);
        }

        // Create or reactivate subscription
        OrganizationApplication subscription;
        if (existingSubscription != null)
        {
            // Reactivate existing subscription
            existingSubscription.Reactivate(request.EnabledBy, request.SubscriptionTier, request.ExpiresAt);
            await _organizationRepository.UpdateApplicationSubscriptionAsync(existingSubscription, cancellationToken);
            subscription = existingSubscription;
        }
        else
        {
            // Create new subscription
            subscription = OrganizationApplication.Create(
                request.OrganizationId,
                request.ApplicationId,
                request.EnabledBy,
                request.SubscriptionTier,
                request.ExpiresAt);

            await _organizationRepository.EnableApplicationAsync(subscription, cancellationToken);
        }

        // Get enabler info
        var enabledByUser = await _userRepository.GetByIdAsync(request.EnabledBy, cancellationToken);

        _logger.LogInformation(
            "Application {ApplicationId} enabled for organization {OrganizationId} by {EnabledBy}",
            request.ApplicationId, request.OrganizationId, request.EnabledBy);

        return new OrganizationApplicationDto
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            ApplicationId = subscription.ApplicationId,
            ApplicationCode = application.Code,
            ApplicationName = application.Name,
            ApplicationDescription = application.Description,
            ApplicationLogoUrl = application.LogoUrl,
            IsActive = subscription.IsActive,
            EnabledAt = subscription.EnabledAt,
            EnabledBy = subscription.EnabledBy,
            EnabledByName = enabledByUser != null ? $"{enabledByUser.FirstName} {enabledByUser.LastName}".Trim() : null,
            ExpiresAt = subscription.ExpiresAt,
            SubscriptionTier = subscription.SubscriptionTier,
            AssignedUserCount = 0
        };
    }
}
