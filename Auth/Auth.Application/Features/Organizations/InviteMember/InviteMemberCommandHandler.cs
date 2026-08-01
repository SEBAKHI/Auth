using System.Globalization;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Organizations.InviteMember;

/// <summary>
/// Handler for inviting a user to an organization.
/// </summary>
public class InviteMemberCommandHandler : IRequestHandler<InviteMemberCommand, ErrorOr<OrganizationInvitationDto>>
{
    private const int InvitationExpirationDays = 7;

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<InviteMemberCommandHandler> _logger;

    public InviteMemberCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ISecureTokenGenerator tokenGenerator,
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<InviteMemberCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _tokenGenerator = tokenGenerator;
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
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

        // The membership role must be organization-level; app roles are assigned separately
        if (role.ApplicationId != null)
        {
            return OrganizationErrors.InvalidMembershipRole(request.RoleId);
        }

        // Same escalation guard as UpdateMemberRole: an org:members:invite holder
        // must not be able to mint an owner (org:*) by inviting a controlled
        // account as org-owner. Ownership transfer is a separate, owner-only op.
        if (string.Equals(role.Code, OrganizationRoleCodes.Owner, StringComparison.OrdinalIgnoreCase))
        {
            return OrganizationErrors.CannotAssignOwnerRole;
        }

        // Get inviter info
        var inviter = await _userRepository.GetByIdAsync(request.InvitedBy, cancellationToken);
        var inviterEmail = inviter?.Email?.Value;
        var requestEmail = request.Email.ToLowerInvariant();

        // Cannot invite self
        if (string.Equals(inviterEmail, requestEmail, StringComparison.OrdinalIgnoreCase))
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

        if (pendingInvitations.Any(i => string.Equals(i.Email.Value, request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return OrganizationErrors.PendingInvitationExists(request.Email);
        }

        // Generate secure token
        var token = _tokenGenerator.Generate();

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

        var inviterName = inviter != null
            ? $"{inviter.FirstName} {inviter.LastName}".Trim()
            : "An administrator";

        // Send from the database-managed template. Language priority: inviter's
        // explicit choice → invitee's profile (matched by email inside the
        // service) → the inviter's request culture as a last-resort hint.
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.OrganizationInvitation,
                RecipientAddress = invitation.Email.Value,
                TriggeredBy = request.InvitedBy,
                LanguageCode = request.LanguageCode,
                LanguageHint = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                Variables = new Dictionary<string, object?>
                {
                    ["OrganizationName"] = organization.Name,
                    ["InviterName"] = inviterName,
                    ["InvitationLink"] = _emailSettings.BuildFrontendUrl(
                        $"/accept-invitation?token={Uri.EscapeDataString(token)}"),
                    ["InvitationToken"] = token,
                    ["ExpiresAt"] = invitation.ExpiresAt
                }
            },
            cancellationToken);

        // Email failure must not fail the command: the token stays available
        // to the admin in the response/UI and can be shared manually.
        if (sendResult.IsError)
        {
            _logger.LogWarning(
                "Failed to send invitation email for invitation {InvitationId}: {Error}; token remains available to admin",
                invitation.Id, sendResult.FirstError.Description);
        }

        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
            Token = token,
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

}
