using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.CreateOrganization;

/// <summary>
/// Handler for creating a new organization.
/// Creates the organization and adds the creator as owner.
/// </summary>
public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, ErrorOr<OrganizationDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<CreateOrganizationCommandHandler> _logger;

    public CreateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ILogger<CreateOrganizationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationDto>> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        // Check for duplicate code
        if (await _organizationRepository.ExistsByCodeAsync(request.Code, cancellationToken))
        {
            return OrganizationErrors.DuplicateCode(request.Code);
        }

        // Get the owner role (null applicationId for organization-level roles)
        var ownerRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Owner, cancellationToken);
        if (ownerRole == null)
        {
            _logger.LogError("Organization owner role '{RoleCode}' not found in database", OrganizationRoleCodes.Owner);
            return Error.Unexpected(
                code: "Organization.OwnerRoleNotFound",
                description: "System configuration error: Organization owner role not found.");
        }

        // Get the user to include their name in response
        var user = await _userRepository.GetByIdAsync(request.CreatedBy, cancellationToken);

        // Create the organization
        var organization = Organization.Create(
            code: request.Code,
            name: request.Name,
            contactEmail: request.ContactEmail,
            ownerId: request.CreatedBy,
            description: request.Description,
            logoUrl: request.LogoUrl,
            website: request.Website);

        await _organizationRepository.CreateAsync(organization, cancellationToken);

        // Add the creator as owner
        var membership = OrganizationUser.Create(
            organization.Id,
            request.CreatedBy,
            ownerRole.Id,
            request.CreatedBy);

        await _organizationRepository.AddMemberAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Organization created: {OrganizationId} ({OrganizationCode}) by {CreatedBy}",
            organization.Id, organization.Code, request.CreatedBy);

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
            OwnerName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
            OwnerEmail = user?.Email?.Value,
            IsActive = organization.IsActive,
            MemberCount = 1,
            EnabledAppCount = 0,
            CreatedAt = organization.CreatedAt,
            CreatedBy = organization.CreatedBy,
            CreatedByName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
            ModifiedAt = organization.ModifiedAt,
            ModifiedBy = organization.ModifiedBy,
            ModifiedByName = null
        };
    }
}
