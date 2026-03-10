using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.AcceptInvitation;

/// <summary>
/// Handler for accepting an organization invitation.
/// </summary>
public class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, ErrorOr<InvitationAcceptResultDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<AcceptInvitationCommandHandler> _logger;

    public AcceptInvitationCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<AcceptInvitationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<InvitationAcceptResultDto>> Handle(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        // Get invitation by token
        var invitation = await _organizationRepository.GetInvitationByTokenAsync(request.Token, cancellationToken);
        if (invitation == null)
        {
            return OrganizationErrors.InvitationNotFoundByToken;
        }

        // Check invitation status
        if (invitation.Status == InvitationStatus.Accepted)
        {
            return OrganizationErrors.InvitationAlreadyAccepted;
        }

        if (invitation.Status == InvitationStatus.Declined)
        {
            return OrganizationErrors.InvitationAlreadyDeclined;
        }

        if (invitation.Status == InvitationStatus.Cancelled)
        {
            return OrganizationErrors.InvitationAlreadyCancelled;
        }

        if (invitation.IsExpired())
        {
            invitation.MarkExpired();
            await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);
            return OrganizationErrors.InvitationExpired;
        }

        // Get accepting user
        var user = await _userRepository.GetByIdAsync(request.AcceptedBy, cancellationToken);
        if (user == null)
        {
            return Error.NotFound(code: "User.NotFound", description: "User not found.");
        }

        // Verify email matches (case-insensitive)
        if (!invitation.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return OrganizationErrors.InvitationEmailMismatch;
        }

        // Get organization
        var organization = await _organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(invitation.OrganizationId);
        }

        if (!organization.IsActive)
        {
            return OrganizationErrors.Inactive(invitation.OrganizationId);
        }

        // Check if already a member
        var existingMembership = await _organizationRepository.GetMembershipAsync(
            invitation.OrganizationId,
            request.AcceptedBy,
            cancellationToken);

        if (existingMembership != null)
        {
            // Already a member - just mark invitation as accepted
            invitation.Accept(request.AcceptedBy);
            await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);

            return new InvitationAcceptResultDto
            {
                Success = true,
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                RoleName = (await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken))?.Name ?? string.Empty,
                Message = "You are already a member of this organization."
            };
        }

        // Create membership
        var membership = OrganizationUser.Create(
            invitation.OrganizationId,
            request.AcceptedBy,
            invitation.RoleId,
            invitation.InvitedBy);

        await _organizationRepository.AddMemberAsync(membership, cancellationToken);

        // Mark invitation as accepted
        invitation.Accept(request.AcceptedBy);
        await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);

        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);

        _logger.LogInformation(
            "User {UserId} accepted invitation and joined organization {OrganizationId} with role {RoleId}",
            request.AcceptedBy, invitation.OrganizationId, invitation.RoleId);

        return new InvitationAcceptResultDto
        {
            Success = true,
            OrganizationId = organization.Id,
            OrganizationName = organization.Name,
            RoleName = role?.Name ?? string.Empty,
            Message = "Successfully joined the organization."
        };
    }
}
