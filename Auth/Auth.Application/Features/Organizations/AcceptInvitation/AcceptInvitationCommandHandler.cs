using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly OrganizationApplicationRoleCheck _applicationRoleCheck;
    private readonly ILogger<AcceptInvitationCommandHandler> _logger;

    public AcceptInvitationCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IRefreshTokenKeyService tokenKeyService,
        OrganizationApplicationRoleCheck applicationRoleCheck,
        ILogger<AcceptInvitationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _tokenKeyService = tokenKeyService;
        _applicationRoleCheck = applicationRoleCheck;
        _logger = logger;
    }

    public async Task<ErrorOr<InvitationAcceptResultDto>> Handle(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        // Get invitation by token
        var invitation = await _organizationRepository.GetInvitationByTokenHashAsync(
            _tokenKeyService.ComputeTokenHash(request.Token), cancellationToken);
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
            return UserErrors.NotFound(request.AcceptedBy);
        }

        // Verify email matches (both are normalized lowercase by Email value object)
        if (invitation.Email != user.Email)
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

        var membership = existingMembership is null
            ? OrganizationUser.Create(
                invitation.OrganizationId,
                request.AcceptedBy,
                invitation.RoleId,
                invitation.InvitedBy)
            : null;

        var (applicationRole, applicationRoleName) = await ResolveApplicationRoleAsync(
            invitation, request.AcceptedBy, alreadyMember: existingMembership is not null, cancellationToken);

        var acceptResult = invitation.Accept(request.AcceptedBy);
        if (acceptResult.IsError)
            return acceptResult.Errors;

        // One transaction: the invitation's status, the membership and the application
        // role land together or not at all. A concurrent acceptance of the same
        // invitation finds it no longer pending and writes nothing.
        if (!await _organizationRepository.AcceptInvitationAsync(
                invitation, membership, applicationRole, cancellationToken))
        {
            return OrganizationErrors.InvitationAlreadyAccepted;
        }

        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);

        if (existingMembership is not null)
        {
            return new InvitationAcceptResultDto
            {
                Success = true,
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                RoleName = role?.Name ?? string.Empty,
                ApplicationRoleName = applicationRoleName,
                Message = "You are already a member of this organization.",
                MessageCode = "Invitation.AlreadyMember"
            };
        }

        _logger.LogInformation(
            "User {UserId} accepted invitation and joined organization {OrganizationId} with role {RoleId}",
            request.AcceptedBy, invitation.OrganizationId, invitation.RoleId);

        return new InvitationAcceptResultDto
        {
            Success = true,
            OrganizationId = organization.Id,
            OrganizationName = organization.Name,
            RoleName = role?.Name ?? string.Empty,
            ApplicationRoleName = applicationRoleName,
            Message = "Successfully joined the organization.",
            MessageCode = "Invitation.Joined"
        };
    }

    /// <summary>
    /// The application role the invitation names, as an assignment to write, or none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Re-checked now, against the inviter, because a week may have passed since the
    /// invitation was sent: the organization may have disabled the application, or the
    /// inviter may have lost the authority to hand the role out. A pending invitation
    /// must not outlive the authority that issued it.
    /// </para>
    /// <para>
    /// <b>Platform authority counts here.</b> The invitation was checked when it was
    /// sent with the token's word; this re-check can only remove the role, never add
    /// one, since the role is fixed. Unioning the inviter's live platform set therefore
    /// cannot hand out authority the inviter's token was minted without. It only
    /// keeps an invitation from a platform operator, who holds nothing inside the
    /// organization, from losing its role.
    /// </para>
    /// <para>
    /// A failed re-check does not fail the acceptance. The invitee still joins with the
    /// membership role, and can be given an application role afterwards. Refusing the
    /// whole acceptance would leave the invitee stuck behind a condition only the
    /// organization can change.
    /// </para>
    /// </remarks>
    private async Task<(OrganizationUserRole? Assignment, string? RoleName)> ResolveApplicationRoleAsync(
        OrganizationInvitation invitation,
        Guid acceptedBy,
        bool alreadyMember,
        CancellationToken cancellationToken)
    {
        if (invitation.ApplicationRole is not { } named)
        {
            return (null, null);
        }

        var assignable = await _applicationRoleCheck.EnsureAssignableAsync(
            invitation.OrganizationId,
            invitation.InvitedBy,
            named.ApplicationId,
            named.RoleId,
            platformAuthorityInToken: true,
            cancellationToken);
        if (assignable.IsError)
        {
            _logger.LogWarning(
                "Invitation {InvitationId} to organization {OrganizationId} named application role {RoleId}, which its inviter {InvitedBy} can no longer assign ({ErrorCode}); the invitee joins without it",
                invitation.Id, invitation.OrganizationId, named.RoleId, invitation.InvitedBy, assignable.FirstError.Code);
            return (null, null);
        }

        var roleName = assignable.Value.Role.Name;

        if (alreadyMember && await _organizationRepository.HasAppRoleAsync(
                invitation.OrganizationId, acceptedBy, named.ApplicationId, named.RoleId, cancellationToken))
        {
            return (null, roleName);
        }

        var assignment = OrganizationUserRole.Create(
            invitation.OrganizationId,
            acceptedBy,
            named.ApplicationId,
            named.RoleId,
            invitation.InvitedBy,
            expiresAt: null);

        return (assignment, roleName);
    }
}
