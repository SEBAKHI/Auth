using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.UpdateOrganization;

/// <summary>
/// Handler for updating an organization.
/// </summary>
public class UpdateOrganizationCommandHandler : IRequestHandler<UpdateOrganizationCommand, ErrorOr<OrganizationDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UpdateOrganizationCommandHandler> _logger;

    public UpdateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        ILogger<UpdateOrganizationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationDto>> Handle(
        UpdateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Update organization properties
        organization.Update(
            request.Name,
            request.ContactEmail,
            request.Description,
            request.LogoUrl,
            request.Website,
            request.ModifiedBy);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
                organization.Activate(request.ModifiedBy);
            else
                organization.Deactivate(request.ModifiedBy);
        }

        await _organizationRepository.UpdateAsync(organization, cancellationToken);

        // Get member and app counts
        var members = await _organizationRepository.GetMembersAsync(request.OrganizationId, cancellationToken);
        var apps = await _organizationRepository.GetEnabledApplicationsAsync(request.OrganizationId, cancellationToken);

        // Get owner info
        var owner = await _userRepository.GetByIdAsync(organization.OwnerId, cancellationToken);

        _logger.LogInformation(
            "Organization updated: {OrganizationId} by {ModifiedBy}",
            organization.Id, request.ModifiedBy);

        return new OrganizationDto
        {
            Id = organization.Id,
            Code = organization.Code,
            Name = organization.Name,
            Description = organization.Description,
            LogoUrl = organization.LogoUrl,
            Website = organization.Website,
            ContactEmail = organization.ContactEmail,
            OwnerId = organization.OwnerId,
            OwnerName = owner != null ? $"{owner.FirstName} {owner.LastName}".Trim() : null,
            OwnerEmail = owner?.Email,
            IsActive = organization.IsActive,
            MemberCount = members.Count,
            EnabledAppCount = apps.Count,
            CreatedAt = organization.CreatedAt,
            ModifiedAt = organization.ModifiedAt
        };
    }
}
