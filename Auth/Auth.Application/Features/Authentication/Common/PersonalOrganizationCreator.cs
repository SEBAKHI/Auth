using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared service that creates a personal organization for a user.
/// Extracted from RegisterCommandHandler for reuse across registration flows.
/// </summary>
public class PersonalOrganizationCreator : IPersonalOrganizationCreator
{
    private const string OrgOwnerRoleCode = "org-owner";

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<PersonalOrganizationCreator> _logger;

    public PersonalOrganizationCreator(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        ILogger<PersonalOrganizationCreator> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        // Get the org-owner role (null applicationId for organization-level roles)
        var ownerRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrgOwnerRoleCode, cancellationToken);
        if (ownerRole == null)
        {
            _logger.LogError(
                "Organization owner role '{RoleCode}' not found in database. Skipping org creation for user {UserId}",
                OrgOwnerRoleCode, user.Id);
            return false;
        }

        // Generate a unique org code
        var orgCode = GenerateOrgCode(user.FirstName, user.LastName);

        // Ensure code uniqueness
        while (await _organizationRepository.ExistsByCodeAsync(orgCode, cancellationToken))
        {
            orgCode = GenerateOrgCode(user.FirstName, user.LastName);
        }

        var organization = Organization.Create(
            code: orgCode,
            name: $"{user.FirstName}'s Organization",
            contactEmail: user.Email,
            ownerId: user.Id,
            isAutoCreated: true);

        await _organizationRepository.CreateAsync(organization, cancellationToken);

        // Add user as org-owner
        var membership = OrganizationUser.Create(
            organizationId: organization.Id,
            userId: user.Id,
            roleId: ownerRole.Id,
            invitedBy: user.Id);

        await _organizationRepository.AddMemberAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Personal organization created: {OrganizationId} ({OrganizationCode}) for user {UserId}",
            organization.Id, organization.Code, user.Id);

        return true;
    }

    private static string GenerateOrgCode(string firstName, string lastName)
    {
        var basePart = $"{firstName}-{lastName}"
            .ToLowerInvariant()
            .Replace(" ", "-");

        // Remove invalid characters (keep only lowercase letters, digits, hyphens)
        var cleanCode = new string(basePart
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray())
            .Trim('-');

        // Append a short unique suffix
        var suffix = Guid.NewGuid().ToString("N")[..6];

        return string.IsNullOrEmpty(cleanCode)
            ? $"org-{suffix}"
            : $"{cleanCode}-{suffix}";
    }
}
