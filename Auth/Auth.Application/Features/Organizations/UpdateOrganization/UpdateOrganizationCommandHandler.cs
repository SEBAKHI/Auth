using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ImageReferenceGuard _imageReferenceGuard;
    private readonly ILogger<UpdateOrganizationCommandHandler> _logger;

    public UpdateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        ImageReferenceGuard imageReferenceGuard,
        ILogger<UpdateOrganizationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _imageReferenceGuard = imageReferenceGuard;
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

        var logo = await _imageReferenceGuard.EnsureCanStoreAsync(
            request.LogoUrl, organization.LogoUrl, request.ModifiedBy, cancellationToken);
        if (logo.IsError)
        {
            return logo.Errors;
        }

        // Update organization properties.
        // Named arguments guard against the parameter-order mismatch that previously
        // swapped Website/ContactEmail/Description (see Organization.Update signature).
        // The form resends the composed URL it last read, so the logo is stored
        // back as its key, as applications already do; external URLs pass through.
        organization.Update(
            name: request.Name,
            description: request.Description,
            logoUrl: _imageUrlComposer.Decompose(request.LogoUrl),
            website: request.Website,
            contactEmail: request.ContactEmail,
            modifiedBy: request.ModifiedBy);

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

        var auditNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            [organization.CreatedBy, organization.ModifiedBy],
            cancellationToken);

        _logger.LogInformation(
            "Organization updated: {OrganizationId} by {ModifiedBy}",
            organization.Id, request.ModifiedBy);

        return new OrganizationDto
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
            ModifiedByName = organization.ModifiedBy.HasValue
                ? auditNames.GetValueOrDefault(organization.ModifiedBy.Value)
                : null,
            ModifiedBy = organization.ModifiedBy
        };
    }
}
