using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using System.Security.Cryptography;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Handler for inviting a user to an organization.
/// </summary>
public class InviteMemberCommandHandler : IRequestHandler<InviteMemberCommand, ErrorOr<OrganizationInvitationDto>>
{
    private const int InvitationExpirationDays = 7;

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<InviteMemberCommandHandler> _logger;

    public InviteMemberCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<InviteMemberCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationInvitationDto>> Handle(
        InviteMemberCommand request,
        CancellationToken cancellationToken)
    {
        // Get organization
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        if (!organization.IsActive)
        {
            return OrganizationErrors.Inactive(request.OrganizationId);
        }

        // Validate role exists
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return OrganizationErrors.RoleNotFound(request.RoleId);
        }

        // Get inviter info
        var inviter = await _userRepository.GetByIdAsync(request.InvitedBy, cancellationToken);
        var inviterEmail = inviter?.Email?.ToLowerInvariant();
        var requestEmail = request.Email.ToLowerInvariant();

        // Cannot invite self
        if (inviterEmail == requestEmail)
        {
            return OrganizationErrors.CannotInviteSelf;
        }

        // Check if user is already a member
        var existingUser = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existingUser != null)
        {
            var existingMembership = await _organizationRepository.GetMembershipAsync(
                request.OrganizationId,
                existingUser.Id,
                cancellationToken);

            if (existingMembership != null)
            {
                return OrganizationErrors.AlreadyMember(existingUser.Id, request.OrganizationId);
            }
        }

        // Check for pending invitation
        var pendingInvitations = await _organizationRepository.GetPendingInvitationsAsync(
            request.OrganizationId,
            cancellationToken);

        if (pendingInvitations.Any(i => i.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return OrganizationErrors.PendingInvitationExists(request.Email);
        }

        // Generate secure token
        var token = GenerateSecureToken();

        // Create invitation
        var invitation = OrganizationInvitation.Create(
            organizationId: request.OrganizationId,
            email: request.Email.ToLowerInvariant(),
            roleId: request.RoleId,
            token: token,
            invitedBy: request.InvitedBy,
            expiresInDays: InvitationExpirationDays);

        await _organizationRepository.CreateInvitationAsync(invitation, cancellationToken);

        _logger.LogInformation(
            "Invitation created for {Email} to organization {OrganizationId} by {InvitedBy}",
            request.Email, request.OrganizationId, request.InvitedBy);

        // TODO: Send invitation email

        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
            OrganizationId = invitation.OrganizationId,
            OrganizationName = organization.Name,
            OrganizationLogoUrl = organization.LogoUrl,
            Email = invitation.Email,
            RoleId = invitation.RoleId,
            RoleCode = role.Code,
            RoleName = role.Name,
            Status = invitation.Status.ToString(),
            ExpiresAt = invitation.ExpiresAt,
            IsExpired = invitation.IsExpired(),
            InvitedBy = invitation.InvitedBy,
            InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : null,
            InvitedByEmail = inviter?.Email,
            AcceptedAt = invitation.AcceptedAt,
            AcceptedByUserId = invitation.AcceptedByUserId,
            CreatedAt = invitation.CreatedAt
        };
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
