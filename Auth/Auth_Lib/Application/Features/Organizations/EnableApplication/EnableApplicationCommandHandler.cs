using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ApplicationEntity = Auth_Lib.Domain.Entities.Application;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.EnableApplication;

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
