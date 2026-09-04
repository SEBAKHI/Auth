using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

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
    private readonly OrganizationSettings _settings;
    private readonly ILogger<CreateOrganizationCommandHandler> _logger;

    public CreateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IOptionsSnapshot<OrganizationSettings> settings,
        ILogger<CreateOrganizationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationDto>> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        // The door, before anything is spent on the caller.
        //
        // This endpoint is authenticated and nothing more, so every signed-in user
        // could mint an organization and become its owner — and the seeded owner
        // role carries org:*, which includes inviting any address. That made the
        // whole user population the population able to reach the invitation
        // surface. The switch does not close the invitation surface, which is
        // closed on its own terms; it decides how many people stand in front of it.
        //
        // Platform administrators pass regardless: the switch governs self-service,
        // not administration. The claim is read at the edge, as the delete path
        // reads it, because a handler has no principal to ask.
        //
        // Ahead of the duplicate-code lookup deliberately, so a closed server does
        // not answer differently for a code that is taken and one that is free.
        if (!request.PlatformScope && !_settings.AllowSelfServiceCreation)
        {
            _logger.LogInformation(
                "Self-service organization creation refused for user {UserId} on a closed server",
                request.CreatedBy);
            return OrganizationErrors.SelfServiceCreationClosed;
        }

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
